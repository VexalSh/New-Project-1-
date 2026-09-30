using Godot;

public static class RoomActivity
{
	public static bool IsPlayerInSameRoom(Node2D self, Node2D player)
	{
		if (player == null || !GodotObject.IsInstanceValid(player))
			return false;

		foreach (Node node in self.GetTree().GetNodesInGroup("camera_rooms"))
		{
			if (node is not CameraRoom2D room) continue;
			if (!room.WorldRect.HasPoint(self.GlobalPosition)) continue;

			return room.WorldRect.HasPoint(player.GlobalPosition);
		}

		return false;
	}
}
