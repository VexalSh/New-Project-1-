using Godot;

public interface IKnockbackable
{
	void ApplyKnockback(Vector2 impulse, float duration);
}
