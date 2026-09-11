/// Work in progress
/// Trying to make something, though this functionality might not exist in the final project
/// depending on how this goes.

using Godot;

public partial class CameraRoom2D : Node2D
{
	public Rect2 WorldRect { get; private set; }

	public override void _Ready()
	{
		AddToGroup("camera_rooms");
		WorldRect = ComputeWorldRect();
	}

	private Rect2 ComputeWorldRect()
	{
		foreach (Node child in GetChildren())
		{
			if (child is CollisionShape2D shapeNode && shapeNode.Shape is RectangleShape2D rectShape)
			{
				Vector2 extents = rectShape.Size / 2f;
				Vector2 center = shapeNode.GlobalPosition;
				return new Rect2(center - extents, extents * 2f);
			}
		}

		GD.PushWarning($"CameraRoom2D '{Name}': no RectangleShape2D child found; this room will have zero size.");
		return new Rect2(GlobalPosition, Vector2.Zero);
	}
}
