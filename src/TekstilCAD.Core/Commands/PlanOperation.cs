namespace TekstilCAD.Core.Commands;

/// <summary>
/// Describes an edit the Desktop applies with its own routines (the grainline and grading-rule transforms live in the Desktop
/// project). Positions are millimetres in document axes (Y grows downward). The plan has already checked the edit with the Core
/// operations, so the numbers are valid.
/// </summary>
public abstract record PlanOperation(Guid PieceId);

/// <summary>Move by a distance (right is +X, down is +Y).</summary>
public sealed record MovePieceOperation(Guid PieceId, double DxMm, double DyMm) : PlanOperation(PieceId);

/// <summary>Move so that the top-left corner of the bounding box lands on (<see cref="XMm"/>, <see cref="YMm"/>); <see cref="DxMm"/>, <see cref="DyMm"/> are the resulting distances.</summary>
public sealed record MovePieceToOperation(Guid PieceId, double XMm, double YMm, double DxMm, double DyMm) : PlanOperation(PieceId);

/// <summary>
/// Rotate about the centre of the bounding box. <see cref="OperationId"/> is the id of the matching entry of the Desktop's
/// orthogonal operation table: "rotate-cw" (90° clockwise), "rotate-ccw" (90° counter-clockwise) or "rotate-half" (180°).
/// <see cref="ClockwiseDegrees"/> is 90, -90 or 180.
/// </summary>
public sealed record RotatePieceOperation(Guid PieceId, int ClockwiseDegrees, string OperationId) : PlanOperation(PieceId);

/// <summary>Mirror across the vertical ("mirror-vertical", left and right swap) or horizontal ("mirror-horizontal") centre line.</summary>
public sealed record MirrorPieceOperation(Guid PieceId, MirrorAxis Axis, string OperationId) : PlanOperation(PieceId);
