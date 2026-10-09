namespace Scribbles.Core;

/// <summary>Uniform-stride page math for scroll sync (thumbs, last page).</summary>
public static class PageScrollMath
{
    public static int PageFromOffset(double verticalOffset, double stride, int pageCount)
    {
        if (pageCount <= 0 || stride <= 0)
            return 1;
        return Math.Clamp((int)(verticalOffset / stride) + 1, 1, pageCount);
    }

    public static double OffsetForPage(int pageNumber, double stride, int pageCount)
    {
        if (pageCount <= 0 || stride <= 0)
            return 0;
        return (Math.Clamp(pageNumber, 1, pageCount) - 1) * stride;
    }
}
