using Pacevite.Api.Infrastructure.Regression;
using TUnit.Core;

namespace Pacevite.Api.Tests.Unit.Prediction;

[Category("Unit")]
public sealed class LinearRegressionTests
{
    [Test]
    public async Task Fit_PerfectDecreasingLine_ReturnsCorrectSlopeInterceptAndRSquared()
    {
        // Arrange
        (double X, double Y)[] points = [(0, 4930), (100, 4730), (200, 4530)];

        // Act
        var result = LinearRegression.Fit(points);

        // Assert
        await Assert.That(result.Slope).IsEqualTo(-2.0).Within(0.0001);
        await Assert.That(result.Intercept).IsEqualTo(4930.0).Within(0.0001);
        await Assert.That(result.RSquared).IsEqualTo(1.0).Within(0.0001);
    }

    [Test]
    public async Task Fit_TwoPoints_ExactLinePassesThroughBoth()
    {
        (double X, double Y)[] points = [(0, 4930), (365, 4500)];
        var result = LinearRegression.Fit(points);

        await Assert.That(LinearRegression.Predict(result, 0)).IsEqualTo(4930.0).Within(1.0);
        await Assert.That(LinearRegression.Predict(result, 365)).IsEqualTo(4500.0).Within(1.0);
    }

    [Test]
    public async Task Fit_IdenticalXValues_ReturnsZeroSlopeAndRSquared()
    {
        (double X, double Y)[] points = [(0, 4930), (0, 4500)];
        var result = LinearRegression.Fit(points);

        await Assert.That(result.Slope).IsEqualTo(0.0).Within(0.0001);
        await Assert.That(result.RSquared).IsEqualTo(0.0).Within(0.0001);
    }

    [Test]
    public async Task Fit_FewerThanTwoPoints_ThrowsArgumentException()
    {
        await Assert.That(() => LinearRegression.Fit([(0, 4930)]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Predict_UsesSloperAndIntercept()
    {
        var result = new LinearRegression.Result(Slope: -2.0, Intercept: 5000.0, RSquared: 1.0);
        await Assert.That(LinearRegression.Predict(result, 100)).IsEqualTo(4800.0).Within(0.0001);
    }
}
