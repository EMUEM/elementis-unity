// MovementKit.cs — Elementis movement kit (reel 3 / reel 6 / reel 8).
//
// Plain class owned by FighterController. Port of the movement half of
// fighter.gd: sprint 1.6x + trail flag, wall-run, double jump = backflip,
// super-jump (sprint + jump), dive pose on long falls, hard landing =
// dust + crouch, slide dodge with i-frames, waterspout surf (Katara).
//
// The kit owns the CharacterController velocity. It does NOT decide actions:
// the controller asks it to jump/dash/dodge/surf and feeds it the move wish
// each FixedUpdate. All VFX/SFX are fired through callbacks on the owner
// (FighterController) so this class stays free of prefab references.
//
// Numbers are fighter.gd verbatim: WALK_SPEED 5.6, JUMP_VELOCITY 7.5,
// GRAVITY 22, ARENA_RADIUS 40, sprint 1.6x, surf 2.2x, dash 15, dodge 12
// (slide 14), wall-run 5.5 along + 4.2 up, super-jump 11, double-jump 10.5.
using UnityEngine;

public class MovementKit
{
    public const float WalkSpeed = 5.6f;
    public const float JumpVelocity = 7.5f;
    public const float SuperJumpVelocity = 11f;
    public const float DoubleJumpVelocity = 10.5f;
    public const float Gravity = 22f;
    public const float ArenaRadius = 40f;
    public const float SprintMultiplier = 1.6f;
    public const float SurfMultiplier = 2.2f;
    public const float DashSpeed = 15f;
    public const float DodgeSpeed = 12f;
    public const float SlideSpeed = 14f;

    // Read-only state for the controller / animation / HUD.
    public Vector3 Velocity { get; private set; }
    public bool IsGrounded { get; private set; }
    public bool Sprinting { get; private set; }
    public bool WallRunning { get; private set; }
    public bool Surfing { get; private set; }
    public bool Diving { get; private set; }
    public bool DodgeSliding { get; private set; }
    public float FlipTimeRemaining { get; private set; }   // > 0 while backflipping
    public float LandCrouchRemaining { get; private set; } // > 0 right after a hard landing
    public float HorizontalSpeed
    {
        get { return new Vector2(Velocity.x, Velocity.z).magnitude; }
    }

    float sprintT;
    float wallRunT;
    float wallRunCd;
    Vector3 wallNormal = Vector3.forward;
    Vector3 wallTangent = Vector3.right;
    Vector3 lastWallNormal = Vector3.forward;
    bool hasWallNormal;
    int jumpsUsed;
    float airTime;
    float peakY;
    float surfT;
    Vector3 dashDir = Vector3.forward;
    Vector3 dodgeDir = Vector3.forward;
    bool wasGroundedBeforeMove = true;

    readonly FighterController owner;

    public MovementKit(FighterController owner)
    {
        this.owner = owner;
        Velocity = Vector3.zero;
        peakY = 0f;
    }

    /// <summary>Called by the controller from OnControllerColliderHit.</summary>
    public void NoteWallHit(Vector3 normal)
    {
        if (normal.y < 0.5f && normal.sqrMagnitude > 0.01f)
        {
            lastWallNormal = normal.normalized;
            hasWallNormal = true;
        }
    }

    /// <summary>Per-FixedUpdate locomotion. Returns nothing; read Velocity.</summary>
    public void UpdateMovement(float dt, Vector3 moveWishWorld, bool canMove,
        bool isBusyAction, bool isBlocking, string action)
    {
        var cc = owner.Controller;
        bool grounded = cc != null && cc.isGrounded;
        wasGroundedBeforeMove = IsGrounded;

        // Gravity / grounding.
        if (!grounded && !WallRunning)
        {
            Velocity = new Vector3(Velocity.x, Velocity.y - Gravity * dt, Velocity.z);
        }
        else if (grounded && Velocity.y < 0f)
        {
            Velocity = new Vector3(Velocity.x, -0.5f, Velocity.z);
        }

        // Sprint: sustained full-tilt ground movement, not while acting/blocking.
        // (StateName is "idle" or "run" while moving on the ground.)
        bool wantSprint = moveWishWorld.sqrMagnitude > 0.9f && grounded
            && (action == "idle" || action == "run") && !isBlocking && !WallRunning;
        if (wantSprint) sprintT += dt; else sprintT = 0f;
        bool wasSprinting = Sprinting;
        Sprinting = sprintT > 0.4f;
        if (Sprinting && !wasSprinting) owner.OnSprintStarted();

        // Surf drain / timeout.
        if (Surfing)
        {
            surfT -= dt;
            owner.DrainManaForSurf(dt);
            if (surfT <= 0f || owner.Mana <= 0f
                || (action != "idle" && action != "block" && action != "surf"))
                EndSurf();
        }

        wallRunCd = Mathf.Max(0f, wallRunCd - dt);
        if (FlipTimeRemaining > 0f) FlipTimeRemaining -= dt;
        if (LandCrouchRemaining > 0f) LandCrouchRemaining -= dt;

        // Horizontal velocity by state.
        float speed = WalkSpeed * owner.Data.runSpeed
            * (Surfing ? SurfMultiplier : (Sprinting ? SprintMultiplier : 1f))
            * (isBlocking ? 0.35f : 1f);
        Vector3 vel = Velocity;

        if (WallRunning)
        {
            vel = wallTangent * 5.5f + Vector3.up * 4.2f - wallNormal * 1.2f;
        }
        else if (action == "dash")
        {
            vel.x = dashDir.x * DashSpeed;
            vel.z = dashDir.z * DashSpeed;
        }
        else if (action == "dodge")
        {
            float s = DodgeSliding ? SlideSpeed : DodgeSpeed;
            vel.x = dodgeDir.x * s;
            vel.z = dodgeDir.z * s;
        }
        else if (isBusyAction)
        {
            // Attacking / casting / hit / KO: rooted, decay horizontal speed.
            vel.x = Mathf.MoveTowards(vel.x, 0f, 30f * dt);
            vel.z = Mathf.MoveTowards(vel.z, 0f, 30f * dt);
        }
        else if (canMove)
        {
            Vector3 wish = moveWishWorld * speed;
            // Closing bonus (fighter.gd): +15% when far from the opponent
            // and already moving toward them (big arena).
            if (owner.Opponent != null && owner.OpponentDistance > 15f && wish.sqrMagnitude > 0.01f)
            {
                Vector3 toOpp = owner.Opponent.transform.position - owner.transform.position;
                toOpp.y = 0f;
                if (toOpp.sqrMagnitude > 0.01f
                    && Vector3.Dot(wish.normalized, toOpp.normalized) > 0.5f)
                    wish *= 1.15f;
            }
            float rate = (new Vector2(wish.x, wish.z).magnitude
                > new Vector2(vel.x, vel.z).magnitude + 0.1f) ? 65f : 45f;
            vel.x = Mathf.MoveTowards(vel.x, wish.x, rate * dt);
            vel.z = Mathf.MoveTowards(vel.z, wish.z, rate * dt);
        }
        else
        {
            vel.x = Mathf.MoveTowards(vel.x, 0f, 45f * dt);
            vel.z = Mathf.MoveTowards(vel.z, 0f, 45f * dt);
        }
        Velocity = vel;

        // Air tracking (pre-move grounded state; landing itself is detected
        // in PostMove, after CharacterController.Move — never here).
        IsGrounded = grounded;
        if (grounded)
        {
            airTime = 0f;
            peakY = owner.transform.position.y;
            Diving = false;
        }
        else
        {
            airTime += dt;
            peakY = Mathf.Max(peakY, owner.transform.position.y);
            Diving = Velocity.y < -3f && (peakY - owner.transform.position.y) > 2f;
        }

        // Wall-run state (checked after the move — isOnWall is only valid then).
        if (WallRunning)
        {
            wallRunT += dt;
            bool onWall = cc != null && (cc.collisionFlags & CollisionFlags.Sides) != 0;
            if (wallRunT > 1.6f || !onWall || moveWishWorld.sqrMagnitude < 0.04f)
                EndWallRun();
        }
        else if (wallRunCd <= 0f && !grounded && cc != null
            && (cc.collisionFlags & CollisionFlags.Sides) != 0
            && (action == "idle" || action == "run") && moveWishWorld.sqrMagnitude > 0.09f && hasWallNormal)
        {
            Vector3 n = lastWallNormal;
            if (Vector3.Dot(moveWishWorld, -n) > 0.3f)
            {
                wallNormal = n;
                wallTangent = Vector3.Cross(Vector3.up, n).normalized;
                if (Vector3.Dot(moveWishWorld, wallTangent) < 0f)
                    wallTangent = -wallTangent;
                WallRunning = true;
                wallRunT = 0f;
                owner.OnWallRunStarted();
            }
        }

        // Arena bounds (fighter.gd ARENA_RADIUS 40).
        Vector3 pos = owner.transform.position;
        Vector2 flat = new Vector2(pos.x, pos.z);
        if (flat.magnitude > ArenaRadius)
        {
            flat = flat.normalized * ArenaRadius;
            owner.transform.position = new Vector3(flat.x, pos.y, flat.y);
        }
    }

    /// <summary>Called by the controller AFTER CharacterController.Move.
    /// Single place where landings are detected (impact speed = the
    /// pre-move fall velocity, still stored in Velocity).</summary>
    public void PostMove()
    {
        var cc = owner.Controller;
        if (cc == null) return;
        bool grounded = cc.isGrounded;
        if (grounded && !wasGroundedBeforeMove)
            OnLanded(Velocity.y);
        IsGrounded = grounded;
    }

    void OnLanded(float impactVy)
    {
        jumpsUsed = 0;
        airTime = 0f;
        peakY = owner.transform.position.y;
        Diving = false;
        if (impactVy < -8f)
        {
            // Hard landing: big white dust puff + low crouch.
            LandCrouchRemaining = 0.4f;
            owner.OnHardLanding(true);
        }
        else if (impactVy < -4f)
        {
            LandCrouchRemaining = 0.2f;
            owner.OnHardLanding(false);
        }
    }

    public bool TryJump()
    {
        if (WallRunning)
        {
            // Wall jump: leap away from the wall (keeps the air jump).
            Velocity = new Vector3(wallNormal.x * 7f, 8.5f, wallNormal.z * 7f);
            EndWallRun();
            jumpsUsed = 1;
            owner.OnJumped(false);
            return true;
        }
        if (IsGrounded)
        {
            // Sprint + jump = super-jump (higher, and the fall becomes a dive).
            bool superJump = Sprinting;
            Velocity = new Vector3(Velocity.x, superJump ? SuperJumpVelocity : JumpVelocity, Velocity.z);
            jumpsUsed = 1;
            owner.OnJumped(superJump);
            return true;
        }
        if (jumpsUsed == 1)
        {
            // Double jump = backflip, bigger (reel 6): higher, longer air.
            Velocity = new Vector3(Velocity.x, DoubleJumpVelocity, Velocity.z);
            jumpsUsed = 2;
            FlipTimeRemaining = 0.45f;
            owner.OnJumped(false);
            return true;
        }
        return false;
    }

    public bool TryDash(Vector3 moveWishWorld, Vector3 towardOpponent)
    {
        dashDir = moveWishWorld.sqrMagnitude > 0.01f
            ? moveWishWorld.normalized
            : (towardOpponent.sqrMagnitude > 0.01f ? towardOpponent.normalized : owner.transform.forward);
        return true;
    }

    public bool TryDodge(Vector3 moveWishWorld, Vector3 towardOpponent)
    {
        dodgeDir = moveWishWorld.sqrMagnitude > 0.01f
            ? moveWishWorld.normalized
            : (towardOpponent.sqrMagnitude > 0.01f ? towardOpponent.normalized : owner.transform.forward);
        // Reel 8 slide: dodging out of a sprint is a longer, lower, faster slide.
        DodgeSliding = Sprinting;
        return true;
    }

    public Vector3 WallTangent { get { return wallTangent; } }
    public Vector3 WallNormal { get { return wallNormal; } }

    /// <summary>Knockback from TakeDamage: added to the current velocity,
    /// then decayed by the movement update (the hit slide).</summary>
    public void AddKnockback(Vector3 kb)
    {
        Velocity = new Vector3(Velocity.x + kb.x, Velocity.y, Velocity.z + kb.z);
    }

    /// <summary>Waterspout surf (Katara traversal, reel 4 Flowbound).</summary>
    public bool TrySurf()
    {
        if (owner.Data.characterId != "katara" || Surfing) return false;
        if (!Sprinting || owner.Mana < 20f) return false;
        Surfing = true;
        surfT = 2.5f;
        owner.SpendMana(20f);
        owner.OnSurfStarted();
        return true;
    }

    public void EndSurf()
    {
        if (!Surfing) return;
        Surfing = false;
        surfT = 0f;
        owner.OnSurfEnded();
    }

    public void EndWallRun()
    {
        if (!WallRunning) return;
        WallRunning = false;
        wallRunCd = 0.8f;
        owner.OnWallRunEnded();
    }

    public void ResetForRound()
    {
        Velocity = Vector3.zero;
        Sprinting = false;
        WallRunning = false;
        Surfing = false;
        Diving = false;
        DodgeSliding = false;
        sprintT = 0f; wallRunT = 0f; wallRunCd = 0f;
        jumpsUsed = 0; airTime = 0f;
        FlipTimeRemaining = 0f; LandCrouchRemaining = 0f;
        peakY = 0f;
    }
}
