using System.Windows;

namespace JunkCleaner.Ui;

/// <summary>
/// Squarified treemap (Bruls, Huizing, van Wijk) — порт логики d3-hierarchy squarify + dice/slice.
/// </summary>
internal static class SquarifiedTreemapLayout
{
    /// <summary>Золотое сечение (как ratio по умолчанию в d3-hierarchy squarify).</summary>
    private static readonly double PhiRatio = (1 + Math.Sqrt(5)) / 2;

    public static Rect[] Layout(IReadOnlyList<double> weights, Rect bounds)
    {
        var n = weights.Count;
        if (n == 0)
            return Array.Empty<Rect>();

        var rects = new Rect[n];
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            for (var i = 0; i < n; i++)
                rects[i] = Rect.Empty;
            return rects;
        }

        var sum = 0.0;
        for (var i = 0; i < n; i++)
            sum += Math.Max(weights[i], 1e-12);
        if (sum <= double.Epsilon)
        {
            for (var i = 0; i < n; i++)
                rects[i] = Rect.Empty;
            return rects;
        }

        var children = new List<TreemapNode>(n);
        for (var i = 0; i < n; i++)
        {
            children.Add(
                new TreemapNode
                {
                    SourceIndex = i,
                    Value = Math.Max(weights[i], 1e-12),
                });
        }

        var root = new TreemapNode { SourceIndex = -1, Children = children };
        root.Value = children.Sum(static c => c.Value);

        SquarifyRatio(PhiRatio, root, bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);

        foreach (var c in children)
        {
            var w = Math.Max(0, c.X1 - c.X0);
            var h = Math.Max(0, c.Y1 - c.Y0);
            rects[c.SourceIndex] = new Rect(c.X0, c.Y0, w, h);
        }

        return rects;
    }

    private static void SquarifyRatio(double ratio, TreemapNode parent, double x0, double y0, double x1, double y1)
    {
        var nodes = parent.Children!;
        var n = nodes.Count;
        var i0 = 0;
        var i1 = 0;
        var value = parent.Value;

        while (i0 < n)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;

            double sumValue;
            do
            {
                if (i1 >= n)
                    return;
                sumValue = nodes[i1].Value;
                i1++;
            }
            while (sumValue <= 0 && i1 < n);

            if (sumValue <= 0)
                return;

            var minValue = sumValue;
            var maxValue = sumValue;
            var alpha = Math.Max(dy / dx, dx / dy) / (value * ratio);
            var beta = sumValue * sumValue * alpha;
            var minRatio = Math.Max(maxValue / beta, beta / minValue);

            while (i1 < n)
            {
                var nodeValue = nodes[i1].Value;
                var nextSum = sumValue + nodeValue;
                if (nodeValue < minValue)
                    minValue = nodeValue;
                if (nodeValue > maxValue)
                    maxValue = nodeValue;

                beta = nextSum * nextSum * alpha;
                var newRatio = Math.Max(maxValue / beta, beta / minValue);
                if (newRatio > minRatio)
                {
                    break;
                }

                sumValue = nextSum;
                minRatio = newRatio;
                i1++;
            }

            var rowNodes = nodes.GetRange(i0, i1 - i0);
            var row = new TreemapNode
            {
                Value = sumValue,
                Children = rowNodes,
            };

            var dice = dx < dy;
            if (dice)
            {
                var yStrip = value > 0 ? y0 + dy * sumValue / value : y1;
                TreemapDice(row, x0, y0, x1, yStrip);
                y0 = yStrip;
            }
            else
            {
                var xStrip = value > 0 ? x0 + dx * sumValue / value : x1;
                TreemapSlice(row, x0, y0, xStrip, y1);
                x0 = xStrip;
            }

            value -= sumValue;
            i0 = i1;
        }
    }

    private static void TreemapDice(TreemapNode parent, double x0, double y0, double x1, double y1)
    {
        var nodes = parent.Children!;
        var n = nodes.Count;
        var denom = parent.Value;
        var k = denom > 0 ? (x1 - x0) / denom : 0;
        var x = x0;

        for (var i = 0; i < n; i++)
        {
            var node = nodes[i];
            var w = node.Value * k;
            node.Y0 = y0;
            node.Y1 = y1;
            node.X0 = x;
            node.X1 = x + w;
            x += w;
        }
    }

    private static void TreemapSlice(TreemapNode parent, double x0, double y0, double x1, double y1)
    {
        var nodes = parent.Children!;
        var n = nodes.Count;
        var denom = parent.Value;
        var k = denom > 0 ? (y1 - y0) / denom : 0;
        var y = y0;

        for (var i = 0; i < n; i++)
        {
            var node = nodes[i];
            var h = node.Value * k;
            node.X0 = x0;
            node.X1 = x1;
            node.Y0 = y;
            node.Y1 = y + h;
            y += h;
        }
    }

    private sealed class TreemapNode
    {
        public int SourceIndex { get; init; }

        public double Value { get; set; }

        public List<TreemapNode>? Children { get; init; }

        public double X0 { get; set; }

        public double Y0 { get; set; }

        public double X1 { get; set; }

        public double Y1 { get; set; }
    }
}
