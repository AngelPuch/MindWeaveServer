using System.Drawing;
using System.Drawing.Drawing2D;

namespace MindWeaveServer.BusinessLogic
{
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

        private const float HALF_DIVISOR = 2f;
        private const int DIRECTION_POSITIVE = 1;
        private const int DIRECTION_NEGATIVE = -1;

        public static JigsawPathResult createJigsawPath(
            int baseWidth,
            int baseHeight,
            JigsawPieceEdges edges,
            int tabSize)
        {
            int offsetX = edges.Left == JigsawEdgeType.Tab ? tabSize : 0;
            int offsetY = edges.Top == JigsawEdgeType.Tab ? tabSize : 0;

            int rightExtra = edges.Right == JigsawEdgeType.Tab ? tabSize : 0;
            int bottomExtra = edges.Bottom == JigsawEdgeType.Tab ? tabSize : 0;

            int totalWidth = baseWidth + offsetX + rightExtra;
            int totalHeight = baseHeight + offsetY + bottomExtra;

            var path = new GraphicsPath();

            PointF currentPoint = new PointF(offsetX, offsetY);

            currentPoint = addHorizontalEdge(path, new EdgeDrawContext
            {
                Start = currentPoint,
                Length = baseWidth,
                EdgeType = edges.Top,
                TabSize = tabSize,
                Reverse = false
            });

            currentPoint = addVerticalEdge(path, new EdgeDrawContext
            {
                Start = currentPoint,
                Length = baseHeight,
                EdgeType = edges.Right,
                TabSize = tabSize,
                Reverse = false
            });

            currentPoint = addHorizontalEdge(path, new EdgeDrawContext
            {
                Start = currentPoint,
                Length = baseWidth,
                EdgeType = edges.Bottom,
                TabSize = tabSize,
                Reverse = true
            });

            addVerticalEdge(path, new EdgeDrawContext
            {
                Start = currentPoint,
                Length = baseHeight,
                EdgeType = edges.Left,
                TabSize = tabSize,
                Reverse = true
            });

            path.CloseFigure();

            return new JigsawPathResult
            {
                Path = path,
                OffsetX = offsetX,
                OffsetY = offsetY,
                TotalWidth = totalWidth,
                TotalHeight = totalHeight
            };
        }

        private static PointF addHorizontalEdge(GraphicsPath path, EdgeDrawContext ctx)
        {
            float direction = ctx.Reverse ? DIRECTION_NEGATIVE : DIRECTION_POSITIVE;
            float endX = ctx.Start.X + (ctx.Length * direction);
            PointF end = new PointF(endX, ctx.Start.Y);

            if (ctx.EdgeType == JigsawEdgeType.Flat)
            {
                path.AddLine(ctx.Start, end);
                return end;
            }

            float tabDirection = ctx.EdgeType == JigsawEdgeType.Tab ? DIRECTION_NEGATIVE : DIRECTION_POSITIVE;
            if (ctx.Reverse)
            {
                tabDirection *= DIRECTION_NEGATIVE;
            }

            float tabDepth = ctx.TabSize * tabDirection;
            float midX = ctx.Start.X + (ctx.Length / HALF_DIVISOR) * direction;

            float neckWidth = ctx.Length * NECK_WIDTH_RATIO;
            float tabWidth = ctx.Length * TAB_WIDTH_RATIO;

            float seg1End = ctx.Start.X + (ctx.Length * SEGMENT_1_END_RATIO) * direction;
            float seg2Start = ctx.Start.X + (ctx.Length * SEGMENT_2_START_RATIO) * direction;

            PointF p1 = new PointF(seg1End, ctx.Start.Y);
            path.AddLine(ctx.Start, p1);

            PointF neckStart = new PointF(midX - (neckWidth / HALF_DIVISOR) * direction, ctx.Start.Y);
            PointF cp1 = new PointF(seg1End + (ctx.Length * CURVE_CONTROL_1_OFFSET) * direction, ctx.Start.Y);
            PointF cp2 = new PointF(neckStart.X - (ctx.Length * CURVE_CONTROL_2_OFFSET) * direction, ctx.Start.Y);
            path.AddBezier(p1, cp1, cp2, neckStart);

            PointF bulbLeft = new PointF(midX - (tabWidth / HALF_DIVISOR) * direction, ctx.Start.Y + tabDepth * BULB_INNER_DEPTH);
            PointF cp3 = new PointF(neckStart.X, ctx.Start.Y + tabDepth * NECK_DEPTH_RATIO);
            PointF cp4 = new PointF(bulbLeft.X - (tabWidth * BULB_CONTROL_OUTER) * direction, ctx.Start.Y + tabDepth * BULB_CONTROL_INNER);
            path.AddBezier(neckStart, cp3, cp4, bulbLeft);

            PointF bulbTop = new PointF(midX, ctx.Start.Y + tabDepth * BULB_MAX_DEPTH);
            PointF bulbRight = new PointF(midX + (tabWidth / HALF_DIVISOR) * direction, ctx.Start.Y + tabDepth * BULB_INNER_DEPTH);

            PointF cp5 = new PointF(bulbLeft.X + (tabWidth * BULB_CONTROL_OUTER) * direction, ctx.Start.Y + tabDepth * BULB_PEAK_DEPTH);
            PointF cp6 = new PointF(bulbTop.X - (tabWidth * BULB_CONTROL_WIDTH) * direction, ctx.Start.Y + tabDepth * BULB_PEAK_CONTROL);
            path.AddBezier(bulbLeft, cp5, cp6, bulbTop);

            PointF cp7 = new PointF(bulbTop.X + (tabWidth * BULB_CONTROL_WIDTH) * direction, ctx.Start.Y + tabDepth * BULB_PEAK_CONTROL);
            PointF cp8 = new PointF(bulbRight.X - (tabWidth * BULB_CONTROL_OUTER) * direction, ctx.Start.Y + tabDepth * BULB_PEAK_DEPTH);
            path.AddBezier(bulbTop, cp7, cp8, bulbRight);

            PointF neckEnd = new PointF(midX + (neckWidth / HALF_DIVISOR) * direction, ctx.Start.Y);
            PointF cp9 = new PointF(bulbRight.X + (tabWidth * BULB_CONTROL_OUTER) * direction, ctx.Start.Y + tabDepth * BULB_CONTROL_INNER);
            PointF cp10 = new PointF(neckEnd.X, ctx.Start.Y + tabDepth * NECK_DEPTH_RATIO);
            path.AddBezier(bulbRight, cp9, cp10, neckEnd);

            PointF p2 = new PointF(seg2Start, ctx.Start.Y);
            PointF cp11 = new PointF(neckEnd.X + (ctx.Length * CURVE_CONTROL_2_OFFSET) * direction, ctx.Start.Y);
            PointF cp12 = new PointF(seg2Start - (ctx.Length * CURVE_CONTROL_1_OFFSET) * direction, ctx.Start.Y);
            path.AddBezier(neckEnd, cp11, cp12, p2);

            path.AddLine(p2, end);

            return end;
        }

        private static PointF addVerticalEdge(GraphicsPath path, EdgeDrawContext ctx)
        {
            float direction = ctx.Reverse ? DIRECTION_NEGATIVE : DIRECTION_POSITIVE;
            float endY = ctx.Start.Y + (ctx.Length * direction);
            PointF end = new PointF(ctx.Start.X, endY);

            if (ctx.EdgeType == JigsawEdgeType.Flat)
            {
                path.AddLine(ctx.Start, end);
                return end;
            }

            float tabDirection = ctx.EdgeType == JigsawEdgeType.Tab ? DIRECTION_POSITIVE : DIRECTION_NEGATIVE;
            if (ctx.Reverse)
            {
                tabDirection *= DIRECTION_NEGATIVE;
            }

            float tabDepth = ctx.TabSize * tabDirection;
            float midY = ctx.Start.Y + (ctx.Length / HALF_DIVISOR) * direction;

            float neckWidth = ctx.Length * NECK_WIDTH_RATIO;
            float tabWidth = ctx.Length * TAB_WIDTH_RATIO;

            float seg1End = ctx.Start.Y + (ctx.Length * SEGMENT_1_END_RATIO) * direction;
            float seg2Start = ctx.Start.Y + (ctx.Length * SEGMENT_2_START_RATIO) * direction;

            PointF p1 = new PointF(ctx.Start.X, seg1End);
            path.AddLine(ctx.Start, p1);

            PointF neckStart = new PointF(ctx.Start.X, midY - (neckWidth / HALF_DIVISOR) * direction);
            PointF cp1 = new PointF(ctx.Start.X, seg1End + (ctx.Length * CURVE_CONTROL_1_OFFSET) * direction);
            PointF cp2 = new PointF(ctx.Start.X, neckStart.Y - (ctx.Length * CURVE_CONTROL_2_OFFSET) * direction);
            path.AddBezier(p1, cp1, cp2, neckStart);

            PointF bulbTop = new PointF(ctx.Start.X + tabDepth * BULB_INNER_DEPTH, midY - (tabWidth / HALF_DIVISOR) * direction);
            PointF cp3 = new PointF(ctx.Start.X + tabDepth * NECK_DEPTH_RATIO, neckStart.Y);
            PointF cp4 = new PointF(ctx.Start.X + tabDepth * BULB_CONTROL_INNER, bulbTop.Y - (tabWidth * BULB_CONTROL_OUTER) * direction);
            path.AddBezier(neckStart, cp3, cp4, bulbTop);

            PointF bulbMid = new PointF(ctx.Start.X + tabDepth * BULB_MAX_DEPTH, midY);
            PointF bulbBottom = new PointF(ctx.Start.X + tabDepth * BULB_INNER_DEPTH, midY + (tabWidth / HALF_DIVISOR) * direction);

            PointF cp5 = new PointF(ctx.Start.X + tabDepth * BULB_PEAK_DEPTH, bulbTop.Y + (tabWidth * BULB_CONTROL_OUTER) * direction);
            PointF cp6 = new PointF(ctx.Start.X + tabDepth * BULB_PEAK_CONTROL, bulbMid.Y - (tabWidth * BULB_CONTROL_WIDTH) * direction);
            path.AddBezier(bulbTop, cp5, cp6, bulbMid);

            PointF cp7 = new PointF(ctx.Start.X + tabDepth * BULB_PEAK_CONTROL, bulbMid.Y + (tabWidth * BULB_CONTROL_WIDTH) * direction);
            PointF cp8 = new PointF(ctx.Start.X + tabDepth * BULB_PEAK_DEPTH, bulbBottom.Y - (tabWidth * BULB_CONTROL_OUTER) * direction);
            path.AddBezier(bulbMid, cp7, cp8, bulbBottom);

            PointF neckEnd = new PointF(ctx.Start.X, midY + (neckWidth / HALF_DIVISOR) * direction);
            PointF cp9 = new PointF(ctx.Start.X + tabDepth * BULB_CONTROL_INNER, bulbBottom.Y + (tabWidth * BULB_CONTROL_OUTER) * direction);
            PointF cp10 = new PointF(ctx.Start.X + tabDepth * NECK_DEPTH_RATIO, neckEnd.Y);
            path.AddBezier(bulbBottom, cp9, cp10, neckEnd);

            PointF p2 = new PointF(ctx.Start.X, seg2Start);
            PointF cp11 = new PointF(ctx.Start.X, neckEnd.Y + (ctx.Length * CURVE_CONTROL_2_OFFSET) * direction);
            PointF cp12 = new PointF(ctx.Start.X, seg2Start - (ctx.Length * CURVE_CONTROL_1_OFFSET) * direction);
            path.AddBezier(neckEnd, cp11, cp12, p2);

            path.AddLine(p2, end);

            return end;
        }
    }

    public class JigsawPathResult
    {
        public GraphicsPath Path { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int TotalWidth { get; set; }
        public int TotalHeight { get; set; }
    }

    public class EdgeDrawContext
    {
        public PointF Start { get; set; }
        public int Length { get; set; }
        public JigsawEdgeType EdgeType { get; set; }
        public int TabSize { get; set; }
        public bool Reverse { get; set; }
    }
}