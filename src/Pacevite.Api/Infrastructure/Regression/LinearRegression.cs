namespace Pacevite.Api.Infrastructure.Regression;

public static class LinearRegression
{
    public sealed record Result(double Slope, double Intercept, double RSquared);

    public static Result Fit(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 2)
            throw new ArgumentException("At least 2 points required.", nameof(points));

        int n = points.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0, sumYY = 0;

        foreach (var (x, y) in points)
        {
            sumX  += x;
            sumY  += y;
            sumXY += x * y;
            sumXX += x * x;
            sumYY += y * y;
        }

        double meanX = sumX / n;
        double meanY = sumY / n;
        double ssXY  = sumXY - n * meanX * meanY;
        double ssXX  = sumXX - n * meanX * meanX;
        double ssYY  = sumYY - n * meanY * meanY;

        double slope     = ssXX == 0 ? 0 : ssXY / ssXX;
        double intercept = meanY - slope * meanX;
        double rSquared  = ssXX == 0 || ssYY == 0 ? 0 : (ssXY * ssXY) / (ssXX * ssYY);

        return new Result(slope, intercept, rSquared);
    }

    public static double Predict(Result regression, double x) =>
        regression.Slope * x + regression.Intercept;
}
