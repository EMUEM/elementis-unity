// SkillProjectile.cs — travelling skill visuals (Water Blast / Fireball /
// Tidal Wave). The VFX prefab is visuals only (VFX-NOTES.md): this
// component moves the root forward and applies the hit. Added at runtime
// by FighterController.ApplySkill1 / ApplySkill4; never authored in a prefab.
using UnityEngine;

public class SkillProjectile : MonoBehaviour
{
    FighterController owner;
    FighterController opponent;
    float damage;
    float knockback;
    float speed;
    float life;
    float initialLife;
    float hitRadius;
    string hitSound;
    string hitVfxName;
    float arcHeight; // REA S4: water arcs (a ballistic rise+fall); fire flies straight (0)
    float vy;        // current vertical velocity of the arc

    /// <summary>direction: world travel direction (the VFX root is rotated to it).
    /// arc: peak height of the ballistic arc in metres (water 1.0-1.2, fire 0).</summary>
    public void Launch(FighterController owner, FighterController opponent,
        float damage, float knockback, float speed, float life, float hitRadius,
        string hitSound, string hitVfxName, Vector3 direction, float arc = 0f)
    {
        this.owner = owner;
        this.opponent = opponent;
        this.damage = damage;
        this.knockback = knockback;
        this.speed = speed;
        this.life = life;
        this.initialLife = life;
        this.hitRadius = hitRadius;
        this.hitSound = hitSound;
        this.hitVfxName = hitVfxName;
        this.arcHeight = arc;
        // Ballistic arc: rise fast, fall back to the launch height by the
        // end of the flight (vy chosen so the arc peaks mid-flight).
        this.vy = arc > 0f ? (2f * arc / Mathf.Max(0.1f, life * 0.5f)) : 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized);
    }

    void FixedUpdate()
    {
        life -= Time.fixedDeltaTime;
        if (life <= 0f)
        {
            Destroy(gameObject);
            return;
        }
        transform.position += transform.forward * speed * Time.fixedDeltaTime;
        // Ballistic arc (water): rise, then fall under gravity. The hit
        // check below is a 3D distance to the opponent's chest, so a
        // modest arc (peak ~1 m) still connects.
        if (arcHeight > 0f)
        {
            vy -= 9.8f * Time.fixedDeltaTime;
            transform.position += Vector3.up * vy * Time.fixedDeltaTime;
        }

        // Arena bounds: a projectile that leaves the arena is gone.
        Vector2 flat = new Vector2(transform.position.x, transform.position.z);
        if (flat.magnitude > MovementKit.ArenaRadius + 2f)
        {
            Destroy(gameObject);
            return;
        }

        if (opponent == null || opponent.IsDead) return;
        Vector3 to = opponent.transform.position + Vector3.up * 1.2f - transform.position;
        if (to.magnitude < hitRadius)
        {
            opponent.TakeDamage(damage, owner != null ? owner.transform.position : transform.position,
                knockback, owner);
            AudioManager.Play(hitSound, 0.9f);
            if (ElementisFx.Instance != null)
                ElementisFx.Instance.Spawn(hitVfxName,
                    opponent.transform.position + Vector3.up * 1.2f, Quaternion.identity);
            Destroy(gameObject);
        }
    }
}
