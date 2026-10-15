using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController))]
public class PlayerAnimator : MonoBehaviour
{
    [Serializable]
    public class AttackBinding
    {
        public Key key;
        [Tooltip("Nome da animação no Spine. Vazio = tecla ainda sem ataque.")]
        public string animation;
    }

    private const int BaseTrack = 0;     // idle / walking / landing
    private const int AttackTrack = 1;   // ataques, tocam por cima da base

    [SerializeField] private SkeletonAnimation skeleton;

    [Header("Animações (nomes no Spine)")]
    [SerializeField] private string idleAnimation = "idle";
    [SerializeField] private string walkAnimation = "walking";
    [SerializeField] private string landingAnimation = "landing";

    [Header("Ataques")]
    [SerializeField] private List<AttackBinding> attackBindings = new List<AttackBinding>
    {
        new AttackBinding { key = Key.A, animation = "attack" },
        new AttackBinding { key = Key.S, animation = "skill" },
        new AttackBinding { key = Key.D },
        new AttackBinding { key = Key.Z },
        new AttackBinding { key = Key.X },
        new AttackBinding { key = Key.C },
    };

    [Header("Dash (sem animação própria: usa a de andar, acelerada)")]
    [SerializeField] private float dashTimeScale = 2.2f;

    [Header("Ataque andando")]
    [Tooltip("Enquanto anda no chão e ataca, as pernas seguem a animação de andar em vez da pose do ataque.")]
    [SerializeField] private bool keepWalkingLegsWhileAttacking = true;
    [SerializeField] private float attackFadeOut = 0.08f;

    private PlayerController controller;
    private string currentLoop;
    private bool landingBusy;
    private Spine.TrackEntry landingEntry;
    private Spine.TrackEntry attackEntry;
    private bool attackActive;
    private bool legsFromWalk;
    private bool wasGrounded = true;

    // esqueleto "sombra" que só toca a animação de andar, para emprestar as pernas ao ataque
    private Spine.Skeleton shadowSkeleton;
    private Spine.Animation shadowWalk;
    private readonly List<Spine.Bone> legBones = new List<Spine.Bone>();
    private readonly List<Spine.Bone> shadowLegBones = new List<Spine.Bone>();
    private readonly List<Spine.IkConstraint> ikConstraints = new List<Spine.IkConstraint>();
    private readonly List<Spine.IkConstraint> shadowIkConstraints = new List<Spine.IkConstraint>();

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        if (skeleton == null) skeleton = GetComponentInChildren<SkeletonAnimation>();
    }

    private void OnEnable()
    {
        if (skeleton != null) skeleton.UpdateLocal += ApplyWalkingLegs;
    }

    private void OnDisable()
    {
        if (skeleton != null) skeleton.UpdateLocal -= ApplyWalkingLegs;
    }

    private void Start()
    {
        PlayLoop(idleAnimation);
    }

    private void Update()
    {
        if (skeleton == null) return;

        bool grounded = controller.IsGrounded;
        bool dashing = controller.IsDashing;
        bool wantsToMove = Mathf.Abs(controller.MoveInput) > 0.01f || dashing || !grounded;

        // A aterrissagem é interrompida na hora por movimento, dash ou pulo (senão parece deslizar agachado).
        if (landingBusy && wantsToMove)
        {
            landingBusy = false;
            landingEntry = null;
            currentLoop = null;
        }

        string attack = ReadAttackKey();
        if (attack != null) PlayAttack(attack);
        else if (!wasGrounded && grounded && !wantsToMove) PlayLanding();
        wasGrounded = grounded;

        bool walkingOnGround = grounded && (dashing || Mathf.Abs(controller.MoveInput) > 0.01f);
        legsFromWalk = keepWalkingLegsWhileAttacking && attackActive && walkingOnGround;

        // A base continua rodando durante o ataque: o personagem não para de andar.
        if (!landingBusy)
            PlayLoop(walkingOnGround ? walkAnimation : idleAnimation);

        var baseEntry = skeleton.AnimationState.GetCurrent(BaseTrack);
        if (baseEntry != null) baseEntry.TimeScale = (dashing && walkingOnGround) ? dashTimeScale : 1f;
    }

    private string ReadAttackKey()
    {
        var kb = Keyboard.current;
        if (kb == null) return null;
        foreach (var binding in attackBindings)
        {
            if (!string.IsNullOrEmpty(binding.animation) && kb[binding.key].wasPressedThisFrame)
                return binding.animation;
        }
        return null;
    }

    private void PlayLoop(string animationName)
    {
        if (currentLoop == animationName) return;
        currentLoop = animationName;
        skeleton.AnimationState.SetAnimation(BaseTrack, animationName, true);
    }

    private void PlayLanding()
    {
        landingBusy = true;
        currentLoop = null;
        var entry = skeleton.AnimationState.SetAnimation(BaseTrack, landingAnimation, false);
        landingEntry = entry;
        entry.Complete += e =>
        {
            // ignora o fim de uma animação que já foi substituída
            if (landingEntry == e) { landingBusy = false; landingEntry = null; }
        };
    }

    private void PlayAttack(string animationName)
    {
        attackActive = true;
        var entry = skeleton.AnimationState.SetAnimation(AttackTrack, animationName, false);
        attackEntry = entry;
        entry.Complete += e =>
        {
            if (attackEntry != e) return;
            attackActive = false;
            attackEntry = null;
            skeleton.AnimationState.SetEmptyAnimation(AttackTrack, attackFadeOut);
        };
    }

    // Roda depois de as animações serem aplicadas e antes do cálculo das posições.
    // Troca as pernas (e os alvos de IK dos pés) pela pose da animação de andar.
    private void ApplyWalkingLegs(ISkeletonAnimation animated)
    {
        if (!legsFromWalk) return;

        var baseEntry = skeleton.AnimationState.GetCurrent(BaseTrack);
        if (baseEntry == null || baseEntry.Animation.Name != walkAnimation) return;
        if (shadowSkeleton == null && !BuildShadow()) return;

        float t = baseEntry.AnimationTime;
        shadowWalk.Apply(shadowSkeleton, t, t, true, null, 1f, Spine.MixBlend.Setup, Spine.MixDirection.In);

        for (int i = 0; i < legBones.Count; i++)
        {
            var b = legBones[i];
            var s = shadowLegBones[i];
            b.X = s.X; b.Y = s.Y; b.Rotation = s.Rotation;
            b.ScaleX = s.ScaleX; b.ScaleY = s.ScaleY;
            b.ShearX = s.ShearX; b.ShearY = s.ShearY;
        }
        for (int i = 0; i < ikConstraints.Count; i++)
        {
            var c = ikConstraints[i];
            var s = shadowIkConstraints[i];
            c.Mix = s.Mix; c.Softness = s.Softness; c.BendDirection = s.BendDirection;
            c.Compress = s.Compress; c.Stretch = s.Stretch;
        }
    }

    private bool BuildShadow()
    {
        var real = skeleton.Skeleton;
        if (real == null) return false;
        shadowWalk = real.Data.FindAnimation(walkAnimation);
        if (shadowWalk == null) return false;
        shadowSkeleton = new Spine.Skeleton(real.Data);

        legBones.Clear(); shadowLegBones.Clear();
        foreach (var bone in real.Bones)
        {
            if (!IsLowerBody(bone.Data.Name)) continue;
            legBones.Add(bone);
            shadowLegBones.Add(shadowSkeleton.FindBone(bone.Data.Name));
        }

        ikConstraints.Clear(); shadowIkConstraints.Clear();
        foreach (var c in real.IkConstraints)
        {
            ikConstraints.Add(c);
            shadowIkConstraints.Add(shadowSkeleton.FindIkConstraint(c.Data.Name));
        }
        return true;
    }

    // Ossos do Turner: l_leg1/l_leg2/l_foot, r_leg2/r_;eg1 (typo do arquivo)/r_foot e os alvos de IK leftleg*/rightleg*.
    private static bool IsLowerBody(string boneName)
    {
        return boneName.StartsWith("l_") || boneName.StartsWith("r_") ||
               boneName.StartsWith("leftleg") || boneName.StartsWith("rightleg");
    }
}
