using Godot;
using System.Collections.Generic;

/// <summary>
/// Attach to a Camera2D placed as a child of the CharacterBody2D you want it to follow.
///
/// Smoothly alters the Zoom to exactly match the target room's aspect ratio/dimensions,
/// ensuring the screen boundaries cleanly adapt without stretching the graphics.
/// </summary>
public partial class PlayerCamera2D : Camera2D
{
	[ExportGroup("Follow")]
	[Export] public bool SmoothFollow = true;
	[Export] public float FollowSpeed = 8f;

	[ExportGroup("Room Fitting")]
	[Export] public bool AutoZoomToFitRoom = true;
	[Export] public float ZoomSmoothSpeed = 6f;

	[ExportGroup("Masking")]
	[Export] public bool DrawBlackBars = true;

	private readonly List<CameraRoom2D> _rooms = new();
	private Node2D _player;

	private Vector2 _currentPos;
	private float _baselineZoom;
	private float _curZoom;
	private CameraRoom2D _activeRoom = null;

	public override void _Ready()
	{
		TopLevel = true;
		PositionSmoothingEnabled = false;

		_player = GetParent() as Node2D;
		_currentPos = _player != null ? _player.GlobalPosition : GlobalPosition;
		GlobalPosition = _currentPos;

		foreach (Node node in GetTree().GetNodesInGroup("camera_rooms"))
		{
			if (node is CameraRoom2D room)
				_rooms.Add(room);
		}

		_baselineZoom = Zoom.X;
		_curZoom = _baselineZoom;
		Zoom = new Vector2(_curZoom, _curZoom);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null) return;
		float dt = (float)delta;

		CameraRoom2D currentRoom = null;
		foreach (CameraRoom2D room in _rooms)
		{
			if (room.ContainsBody(_player))
			{
				currentRoom = room;
				break;
			}
		}
		_activeRoom = currentRoom;

		// --- Smoothly Resize Camera View to Fit Exact Room Dimensions ---
		float targetZoom = _baselineZoom;
		if (AutoZoomToFitRoom && currentRoom != null)
		{
			Vector2 roomSize = currentRoom.WorldRect.Size;
			if (roomSize.X > 0f && roomSize.Y > 0f)
			{
				Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
				
				float scaleX = viewportSize.X / roomSize.X;
				float scaleY = viewportSize.Y / roomSize.Y;
				
				// Using Mathf.Min fits the ENTIRE room exactly inside the screen bounds.
				// The axis that doesn't fit perfectly will be letterboxed/pillarboxed (black bars)
				// assuming nothing is drawn outside of the room bounds.
				targetZoom = Mathf.Min(scaleX, scaleY);
			}
		}

		_curZoom = Mathf.Lerp(_curZoom, targetZoom, ExpSmoothFactor(ZoomSmoothSpeed, dt));
		Zoom = new Vector2(_curZoom, _curZoom);

		// --- Track and Clamp Position ---
		Vector2 halfView = GetViewport().GetVisibleRect().Size / (2f * Mathf.Max(_curZoom, 0.0001f));
		Vector2 desired = _player.GlobalPosition;

		if (currentRoom != null)
		{
			Rect2 r = currentRoom.WorldRect;

			// Handle X clamping or centering if view matches room perfectly
			float minX = r.Position.X + halfView.X;
			float maxX = r.End.X - halfView.X;
			desired.X = minX <= maxX ? Mathf.Clamp(desired.X, minX, maxX) : r.Position.X + (r.Size.X * 0.5f);

			// Handle Y clamping or centering if view matches room perfectly
			float minY = r.Position.Y + halfView.Y;
			float maxY = r.End.Y - halfView.Y;
			desired.Y = minY <= maxY ? Mathf.Clamp(desired.Y, minY, maxY) : r.Position.Y + (r.Size.Y * 0.5f);
		}

		_currentPos = SmoothFollow
			? _currentPos.Lerp(desired, ExpSmoothFactor(FollowSpeed, dt))
			: desired;

		GlobalPosition = _currentPos;

		// Force the canvas redraw to update the black bars position
		if (DrawBlackBars)
		{
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		if (!DrawBlackBars || _activeRoom == null) return;

		// Get the current visible rectangle in camera space
		Rect2 screenRect = GetViewport().GetVisibleRect();
		Vector2 viewSize = screenRect.Size / _curZoom;
		
		// Map the camera screen edges into global world space coordinates
		Vector2 topLeft = GlobalPosition - (viewSize * 0.5f);
		Vector2 bottomRight = GlobalPosition + (viewSize * 0.5f);
		
		Rect2 roomRect = _activeRoom.WorldRect;
		Color barColor = Colors.Black;

		// Transform 2D canvas drawing to use absolute global world coordinates
		DrawSetTransform(-GlobalPosition, 0f, Vector2.One);

		// Left bar
		if (topLeft.X < roomRect.Position.X)
		{
			DrawRect(new Rect2(topLeft.X, topLeft.Y, roomRect.Position.X - topLeft.X, viewSize.Y), barColor);
		}
		// Right bar
		if (bottomRight.X > roomRect.End.X)
		{
			DrawRect(new Rect2(roomRect.End.X, topLeft.Y, bottomRight.X - roomRect.End.X, viewSize.Y), barColor);
		}
		// Top bar
		if (topLeft.Y < roomRect.Position.Y)
		{
			DrawRect(new Rect2(topLeft.X, topLeft.Y, viewSize.X, roomRect.Position.Y - topLeft.Y), barColor);
		}
		// Bottom bar
		if (bottomRight.Y > roomRect.End.Y)
		{
			DrawRect(new Rect2(topLeft.X, roomRect.End.Y, viewSize.X, bottomRight.Y - roomRect.End.Y), barColor);
		}
	}

	private static float ExpSmoothFactor(float speed, float dt) => 1f - Mathf.Exp(-speed * dt);
}
