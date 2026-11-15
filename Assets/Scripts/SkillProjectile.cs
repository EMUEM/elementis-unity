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
    float hitRadius;
    string hitSound;
    string hitVfxName;

    /// <summary>direction: world travel direction (the VFX root is rotated to it).</summary>
    public void Launch(FighterController owner, FighterController opponent,
        float damage, float knockback, float speed, float life, float hitRadius,
        string hitSound, string hitVfxName, Vector3 direction)
    {
        this.owner = owner;
        this.opponent = opponent;
        this.damage = damage;
        this.knockback = knockback;
        this.speed = speed;
        this.life = life;
        this.hitRadius = hitRadius;
        this.hitSound = hitSound;
        this.hitVfxName = hitVfxName;
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
