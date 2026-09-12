using Godot;

/// <summary>
/// Marks a room/region for PlayerCamera2D. Add as many RectangleShape2D-based
/// CollisionShape2D children as you like - they're all treated as ONE room
/// (their bounding boxes are unioned together for the camera's clamp
/// rectangle), which lets a single room be an irregular/composite shape
/// (e.g. an L-shaped room made of two rectangles).
///
/// Detection of "is the player in this room" uses real Area2D physics
/// overlap (GetOverlappingBodies), not manual point-in-rect math - this
/// correctly respects every shape's actual position/rotation/scale instead
/// of an approximated rectangle, which is what caused misalignment before.
///
/// Setup:
///   - Node type: Area2D
///   - One or more CollisionShape2D children, each with a RectangleShape2D
///     (axis-aligned - rotation isn't accounted for in the bounding rect).
///   - Collision Layer/Mask: set the mask so this area detects the player's
///     physics layer (Monitoring must stay on, which is the default).
/// </summary>
public partial class CameraRoom2D : Area2D
{
	/// <summary>Union of all child shapes' world-space bounding boxes. Used as
	/// the Camera2D clamp rectangle - not for overlap detection.</summary>
	public Rect2 WorldRect { get; private set; }

	public override void _Ready()
	{
		AddToGroup("camera_rooms");
		Monitoring = true;
		WorldRect = ComputeWorldRect();
	}

	private Rect2 ComputeWorldRect()
	{
		Rect2 result = default;
		bool any = false;

		foreach (Node child in GetChildren())
		{
			if (child is not CollisionShape2D shapeNode || shapeNode.Shape is not RectangleShape2D rectShape)
				continue;

			Vector2 scale = shapeNode.GlobalScale;
			Vector2 extents = rectShape.Size * scale / 2f;
			Vector2 center = shapeNode.GlobalPosition;
			Rect2 shapeRect = new Rect2(center - extents, extents * 2f);

			result = any ? result.Merge(shapeRect) : shapeRect;
			any = true;
		}

		if (!any)
		{
			GD.PushWarning($"CameraRoom2D '{Name}': no RectangleShape2D children found; room has zero size.");
			return new Rect2(GlobalPosition, Vector2.Zero);
		}

		return result;
	}

	/// <summary>
	/// True if the given body is currently physically overlapping ANY of this
	/// room's collision shapes. Relies on Godot's own physics overlap query,
	/// so composite/irregular room shapes work correctly.
	/// </summary>
	public bool ContainsBody(Node2D body)
	{
		return GetOverlappingBodies().Contains(body);
	}
}
