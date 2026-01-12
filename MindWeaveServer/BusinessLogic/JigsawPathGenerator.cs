using MindWeaveServer.BusinessLogic;
using System.Drawing;
using System.Drawing.Drawing2D;

public static class JigsawPathGenerator
{
    private const float TAB_WIDTH_RATIO = 0.35f;
    private const float NECK_WIDTH_RATIO = 0.15f;

    private const float SEGMENT_1_END_RATIO = 0.35f;
    private const float SEGMENT_2_START_RATIO = 0.65f;

    private const float CURVE_CONTROL_1_OFFSET = 0.05f;
    private const float CURVE_CONTROL_2_OFFSET = 0.02f;

    private const float NECK_DEPTH_RATIO = 0.2f;

    private const float BULB_INNER_DEPTH = 0.7f;
    private const float BULB_CONTROL_INNER = 0.4f;
    private const float BULB_CONTROL_OUTER = 0.1f;

    private const float BULB_MAX_DEPTH = 1.0f;
    private const float BULB_PEAK_DEPTH = 1.1f;
    private const float BULB_PEAK_CONTROL = 1.15f;
    private const float BULB_CONTROL_WIDTH = 0.25f;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell",
    "S107:Methods should not have too many parameters",
    Justification = "Method requires multiple out parameters for dimension calculations - refactoring would reduce readability")]
    public static GraphicsPath createJigsawPath(
        int baseWidth,
        int baseHeight,
        JigsawPieceEdges edges,
        int tabSize,
        out int offsetX,
        out int offsetY,
        out int totalWidth,
        out int totalHeight)
    {
        offsetX = edges.Left == JigsawEdgeType.Tab ? tabSize : 0;
        offsetY = edges.Top == JigsawEdgeType.Tab ? tabSize : 0;

        int rightExtra = edges.Right == JigsawEdgeType.Tab ? tabSize : 0;
        int bottomExtra = edges.Bottom == JigsawEdgeType.Tab ? tabSize : 0;

        totalWidth = baseWidth + offsetX + rightExtra;
        totalHeight = baseHeight + offsetY + bottomExtra;

        var path = new GraphicsPath();

        float startX = offsetX;
        float startY = offsetY;

        PointF currentPoint = new PointF(startX, startY);

        currentPoint = addHorizontalEdge(path, currentPoint, baseWidth, edges.Top, tabSize, false);
        currentPoint = addVerticalEdge(path, currentPoint, baseHeight, edges.Right, tabSize, false);
        currentPoint = addHorizontalEdge(path, currentPoint, baseWidth, edges.Bottom, tabSize, true);
        addVerticalEdge(path, currentPoint, baseHeight, edges.Left, tabSize, true);

        path.CloseFigure();

        return path;
    }

    private static PointF addHorizontalEdge(GraphicsPath path, PointF start, int length, JigsawEdgeType edgeType, int tabSize, bool reverse)
    {
        float direction = reverse ? -1 : 1;
        float endX = start.X + (length * direction);
        PointF end = new PointF(endX, start.Y);

        if (edgeType == JigsawEdgeType.Flat)
        {
            path.AddLine(start, end);
            return end;
        }

        float tabDirection = edgeType == JigsawEdgeType.Tab ? -1 : 1;
        if (reverse) tabDirection *= -1;

        float tabDepth = tabSize * tabDirection;
        float midX = start.X + (length / 2f) * direction;

        float neckWidth = length * NECK_WIDTH_RATIO;
        float tabWidth = length * TAB_WIDTH_RATIO;

        float seg1End = start.X + (length * SEGMENT_1_END_RATIO) * direction;
        float seg2Start = start.X + (length * SEGMENT_2_START_RATIO) * direction;

        PointF p1 = new PointF(seg1End, start.Y);
        path.AddLine(start, p1);

        PointF neckStart = new PointF(midX - (neckWidth / 2f) * direction, start.Y);
        PointF cp1 = new PointF(seg1End + (length * CURVE_CONTROL_1_OFFSET) * direction, start.Y);
        PointF cp2 = new PointF(neckStart.X - (length * CURVE_CONTROL_2_OFFSET) * direction, start.Y);
        path.AddBezier(p1, cp1, cp2, neckStart);

        PointF bulbLeft = new PointF(midX - (tabWidth / 2f) * direction, start.Y + tabDepth * BULB_INNER_DEPTH);
        PointF cp3 = new PointF(neckStart.X, start.Y + tabDepth * NECK_DEPTH_RATIO);
        PointF cp4 = new PointF(bulbLeft.X - (tabWidth * BULB_CONTROL_OUTER) * direction, start.Y + tabDepth * BULB_CONTROL_INNER);
        path.AddBezier(neckStart, cp3, cp4, bulbLeft);

        PointF bulbTop = new PointF(midX, start.Y + tabDepth * BULB_MAX_DEPTH);
        PointF bulbRight = new PointF(midX + (tabWidth / 2f) * direction, start.Y + tabDepth * BULB_INNER_DEPTH);

        PointF cp5 = new PointF(bulbLeft.X + (tabWidth * BULB_CONTROL_OUTER) * direction, start.Y + tabDepth * BULB_PEAK_DEPTH);
        PointF cp6 = new PointF(bulbTop.X - (tabWidth * BULB_CONTROL_WIDTH) * direction, start.Y + tabDepth * BULB_PEAK_CONTROL);
        path.AddBezier(bulbLeft, cp5, cp6, bulbTop);

        PointF cp7 = new PointF(bulbTop.X + (tabWidth * BULB_CONTROL_WIDTH) * direction, start.Y + tabDepth * BULB_PEAK_CONTROL);
        PointF cp8 = new PointF(bulbRight.X - (tabWidth * BULB_CONTROL_OUTER) * direction, start.Y + tabDepth * BULB_PEAK_DEPTH);
        path.AddBezier(bulbTop, cp7, cp8, bulbRight);

        PointF neckEnd = new PointF(midX + (neckWidth / 2f) * direction, start.Y);
        PointF cp9 = new PointF(bulbRight.X + (tabWidth * BULB_CONTROL_OUTER) * direction, start.Y + tabDepth * BULB_CONTROL_INNER);
        PointF cp10 = new PointF(neckEnd.X, start.Y + tabDepth * NECK_DEPTH_RATIO);
        path.AddBezier(bulbRight, cp9, cp10, neckEnd);

        PointF p2 = new PointF(seg2Start, start.Y);
        PointF cp11 = new PointF(neckEnd.X + (length * CURVE_CONTROL_2_OFFSET) * direction, start.Y);
        PointF cp12 = new PointF(seg2Start - (length * CURVE_CONTROL_1_OFFSET) * direction, start.Y);
        path.AddBezier(neckEnd, cp11, cp12, p2);

        path.AddLine(p2, end);

        return end;
    }

    private static PointF addVerticalEdge(GraphicsPath path, PointF start, int length, JigsawEdgeType edgeType, int tabSize, bool reverse)
    {
        float direction = reverse ? -1 : 1;
        float endY = start.Y + (length * direction);
        PointF end = new PointF(start.X, endY);

        if (edgeType == JigsawEdgeType.Flat)
        {
            path.AddLine(start, end);
            return end;
        }

        float tabDirection = edgeType == JigsawEdgeType.Tab ? 1 : -1;
        if (reverse) tabDirection *= -1;

        float tabDepth = tabSize * tabDirection;
        float midY = start.Y + (length / 2f) * direction;

        float neckWidth = length * NECK_WIDTH_RATIO;
        float tabWidth = length * TAB_WIDTH_RATIO;

        float seg1End = start.Y + (length * SEGMENT_1_END_RATIO) * direction;
        float seg2Start = start.Y + (length * SEGMENT_2_START_RATIO) * direction;

        PointF p1 = new PointF(start.X, seg1End);
        path.AddLine(start, p1);

        PointF neckStart = new PointF(start.X, midY - (neckWidth / 2f) * direction);
        PointF cp1 = new PointF(start.X, seg1End + (length * CURVE_CONTROL_1_OFFSET) * direction);
        PointF cp2 = new PointF(start.X, neckStart.Y - (length * CURVE_CONTROL_2_OFFSET) * direction);
        path.AddBezier(p1, cp1, cp2, neckStart);

        PointF bulbTop = new PointF(start.X + tabDepth * BULB_INNER_DEPTH, midY - (tabWidth / 2f) * direction);
        PointF cp3 = new PointF(start.X + tabDepth * NECK_DEPTH_RATIO, neckStart.Y);
        PointF cp4 = new PointF(start.X + tabDepth * BULB_CONTROL_INNER, bulbTop.Y - (tabWidth * BULB_CONTROL_OUTER) * direction);
        path.AddBezier(neckStart, cp3, cp4, bulbTop);

        PointF bulbMid = new PointF(start.X + tabDepth * BULB_MAX_DEPTH, midY);
        PointF bulbBottom = new PointF(start.X + tabDepth * BULB_INNER_DEPTH, midY + (tabWidth / 2f) * direction);

        PointF cp5 = new PointF(start.X + tabDepth * BULB_PEAK_DEPTH, bulbTop.Y + (tabWidth * BULB_CONTROL_OUTER) * direction);
        PointF cp6 = new PointF(start.X + tabDepth * BULB_PEAK_CONTROL, bulbMid.Y - (tabWidth * BULB_CONTROL_WIDTH) * direction);
        path.AddBezier(bulbTop, cp5, cp6, bulbMid);

        PointF cp7 = new PointF(start.X + tabDepth * BULB_PEAK_CONTROL, bulbMid.Y + (tabWidth * BULB_CONTROL_WIDTH) * direction);
        PointF cp8 = new PointF(start.X + tabDepth * BULB_PEAK_DEPTH, bulbBottom.Y - (tabWidth * BULB_CONTROL_OUTER) * direction);
        path.AddBezier(bulbMid, cp7, cp8, bulbBottom);

        PointF neckEnd = new PointF(start.X, midY + (neckWidth / 2f) * direction);
        PointF cp9 = new PointF(start.X + tabDepth * BULB_CONTROL_INNER, bulbBottom.Y + (tabWidth * BULB_CONTROL_OUTER) * direction);
        PointF cp10 = new PointF(start.X + tabDepth * NECK_DEPTH_RATIO, neckEnd.Y);
        path.AddBezier(bulbBottom, cp9, cp10, neckEnd);

        PointF p2 = new PointF(start.X, seg2Start);
        PointF cp11 = new PointF(start.X, neckEnd.Y + (length * CURVE_CONTROL_2_OFFSET) * direction);
        PointF cp12 = new PointF(start.X, seg2Start - (length * CURVE_CONTROL_1_OFFSET) * direction);
        path.AddBezier(neckEnd, cp11, cp12, p2);

        path.AddLine(p2, end);

        return end;
    }
}