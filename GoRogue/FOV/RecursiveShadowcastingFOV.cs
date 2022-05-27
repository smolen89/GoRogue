﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace GoRogue.FOV
{
    /// <summary>
    /// Implements <see cref="IFOV"/> by using a recursive shadow-casting implementation.
    /// </summary>
    /// <remarks>
    /// Compared to the functionally equivalent implementation of recursive shadowcasting, <see cref="RecursiveShadowcastingDoubleBasedFOV"/>, this version
    /// is usually a reasonable default.  However, this version can perform poorly when there are a lot of FOV calculations being appended together (via multiple
    /// calls to CalculateAppend) and you are using DoubleResultView a lot; in those cases, the other implementation may be a better choice.
    /// </remarks>
    [PublicAPI]
    public class RecursiveShadowcastingFOV : BooleanBasedFOVBase
    {
        private HashSet<Point> _currentFOV;
        private HashSet<Point> _previousFOV;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="transparencyView">
        /// The values used to calculate field of view. Values of true are considered
        /// non-blocking (transparent) to line of sight, while false values are considered
        /// to be blocking.
        /// </param>
        /// <param name="hasher">The hashing algorithm to use for points in hash sets.  Defaults to the default hash algorithm for Points.</param>
        public RecursiveShadowcastingFOV(IGridView<bool> transparencyView, IEqualityComparer<Point>? hasher = null)
            : base(transparencyView, new BitArrayView(transparencyView.Width, transparencyView.Height))
        {
            hasher ??= EqualityComparer<Point>.Default;

            _currentFOV = new HashSet<Point>(hasher);
            _previousFOV = new HashSet<Point>(hasher);
        }

        /// <inheritdoc />
        public override IEnumerable<Point> CurrentFOV => _currentFOV;

        /// <inheritdoc />
        public override IEnumerable<Point> NewlySeen => _currentFOV.Where(pos => !_previousFOV.Contains(pos));

        /// <inheritdoc />
        public override IEnumerable<Point> NewlyUnseen => _previousFOV.Where(pos => !_currentFOV.Contains(pos));

        /// <inheritdoc/>
        protected override void OnCalculate(int originX, int originY, double radius, Distance distanceCalc)
        {
            radius = Math.Max(1, radius);

            ResultView[originX, originY] = true;
            _currentFOV.Add(new Point(originX, originY));

            foreach (var d in AdjacencyRule.Diagonals.DirectionsOfNeighbors())
            {
                ShadowCast(1, 1.0, 0.0, 0, d.DeltaX, d.DeltaY, 0, radius, originX, originY,ResultView, _currentFOV,
                    TransparencyView, distanceCalc);
                ShadowCast(1, 1.0, 0.0, d.DeltaX, 0, 0, d.DeltaY, radius, originX, originY, ResultView, _currentFOV,
                    TransparencyView, distanceCalc);
            }
        }

        /// <inheritdoc/>
        protected override void OnCalculate(int originX, int originY, double radius, Distance distanceCalc, double angle, double span)
        {
            radius = Math.Max(1, radius);

            // Convert from 0 pointing up to 0 pointing right, which is what is expected by the ShadowCastLimited
            // implementation
            angle -= 90;

            // Convert to radians
            angle = (angle > 360.0 || angle < 0 ? MathHelpers.WrapAround(angle, 360.0) : angle) *
                    SadRogue.Primitives.MathHelpers.DegreePctOfCircle;
            span *= SadRogue.Primitives.MathHelpers.DegreePctOfCircle;

            ResultView[originX, originY] = true;
            _currentFOV.Add(new Point(originX, originY));

            ShadowCastLimited(1, 1.0, 0.0, 0, 1, 1, 0, radius, originX, originY, ResultView, _currentFOV, TransparencyView,
                distanceCalc, angle, span);
            ShadowCastLimited(1, 1.0, 0.0, 1, 0, 0, 1, radius, originX, originY, ResultView, _currentFOV, TransparencyView,
                distanceCalc, angle, span);

            ShadowCastLimited(1, 1.0, 0.0, 0, -1, 1, 0, radius, originX, originY, ResultView, _currentFOV, TransparencyView,
                distanceCalc, angle, span);
            ShadowCastLimited(1, 1.0, 0.0, -1, 0, 0, 1, radius, originX, originY, ResultView, _currentFOV, TransparencyView,
                distanceCalc, angle, span);

            ShadowCastLimited(1, 1.0, 0.0, 0, -1, -1, 0, radius, originX, originY, ResultView, _currentFOV, TransparencyView,
                distanceCalc, angle, span);
            ShadowCastLimited(1, 1.0, 0.0, -1, 0, 0, -1, radius, originX, originY, ResultView, _currentFOV, TransparencyView,
                distanceCalc, angle, span);

            ShadowCastLimited(1, 1.0, 0.0, 0, 1, -1, 0, radius, originX, originY, ResultView, _currentFOV, TransparencyView,
                distanceCalc, angle, span);
            ShadowCastLimited(1, 1.0, 0.0, 1, 0, 0, -1, radius, originX, originY, ResultView, _currentFOV, TransparencyView,
                distanceCalc, angle, span);
        }

        /// <inheritdoc/>
        protected override void OnReset()
        {
            // Reset visibility
            if (ResultView.Width != TransparencyView.Width || ResultView.Height != TransparencyView.Height)
                ResultView = new BitArrayView(TransparencyView.Width, TransparencyView.Height);
            else
                ((BitArrayView)ResultView).Fill(false);

            // Cycle current and previous FOVs
            (_previousFOV, _currentFOV) = (_currentFOV, _previousFOV);
            _currentFOV.Clear();
        }

        private static void ShadowCast(int row, double start, double end, int xx, int xy, int yx, int yy,
                                       double radius, int startX, int startY, ISettableGridView<bool> lightMap,
                                       HashSet<Point> fovSet,
                                       IGridView<bool> map, Distance distanceStrategy)
        {
            double newStart = 0;
            if (start < end)
                return;

            var blocked = false;
            for (var distance = row; distance <= radius && distance < map.Width + map.Height && !blocked; distance++)
            {
                var deltaY = -distance;
                for (var deltaX = -distance; deltaX <= 0; deltaX++)
                {
                    var currentX = startX + deltaX * xx + deltaY * xy;
                    var currentY = startY + deltaX * yx + deltaY * yy;
                    double leftSlope = (deltaX - 0.5f) / (deltaY + 0.5f);
                    double rightSlope = (deltaX + 0.5f) / (deltaY - 0.5f);

                    if (!(currentX >= 0 && currentY >= 0 && currentX < map.Width && currentY < map.Height) ||
                        start < rightSlope)
                        continue;
                    if (end > leftSlope)
                        break;

                    var deltaRadius = distanceStrategy.Calculate(deltaX, deltaY);
                    // If within lightable area, light if needed
                    if (deltaRadius <= radius && !lightMap[currentX, currentY])
                    {
                        lightMap[currentX, currentY] = true;
                        fovSet.Add(new Point(currentX, currentY));
                        
                    }

                    if (blocked) // Previous cell was blocked
                    {
                        if (!map[currentX, currentY]) // Hit a wall...
                            newStart = rightSlope;
                        else
                        {
                            blocked = false;
                            start = newStart;
                        }
                    }
                    else
                    {
                        if (map[currentX, currentY] || !(distance < radius)) continue;

                        blocked = true;
                        ShadowCast(distance + 1, start, leftSlope, xx, xy, yx, yy, radius, startX, startY,
                            lightMap, fovSet, map, distanceStrategy);
                        newStart = rightSlope;
                    }
                }
            }
        }

        private static void ShadowCastLimited(int row, double start, double end, int xx, int xy, int yx, int yy,
                                              double radius, int startX, int startY,
                                              ISettableGridView<bool> lightMap, HashSet<Point> fovSet, IGridView<bool> map,
                                              Distance distanceStrategy, double angle, double span)
        {
            double newStart = 0;
            if (start < end)
                return;

            var blocked = false;
            for (var distance = row; distance <= radius && distance < map.Width + map.Height && !blocked; distance++)
            {
                var deltaY = -distance;
                for (var deltaX = -distance; deltaX <= 0; deltaX++)
                {
                    var currentX = startX + deltaX * xx + deltaY * xy;
                    var currentY = startY + deltaX * yx + deltaY * yy;
                    double leftSlope = (deltaX - 0.5f) / (deltaY + 0.5f);
                    double rightSlope = (deltaX + 0.5f) / (deltaY - 0.5f);

                    if (!(currentX >= 0 && currentY >= 0 && currentX < map.Width && currentY < map.Height) ||
                        start < rightSlope)
                        continue;
                    if (end > leftSlope)
                        break;

                    var deltaRadius = distanceStrategy.Calculate(deltaX, deltaY);
                    var at2 = Math.Abs(angle - MathHelpers.ScaledAtan2Approx(currentY - startY, currentX - startX));

                    // Check if within lightable area, light if needed
                    if (deltaRadius <= radius && (at2 <= span * 0.5 || at2 >= 1.0 - span * 0.5))
                    {
                        if (!lightMap[currentX, currentY])
                        {
                            lightMap[currentX, currentY] = true;
                            fovSet.Add(new Point(currentX, currentY));
                        }
                    }

                    if (blocked) // Previous cell was blocking
                    {
                        if (!map[currentX, currentY]) // We hit a wall...
                            newStart = rightSlope;
                        else
                        {
                            blocked = false;
                            start = newStart;
                        }
                    }
                    else if (!map[currentX, currentY] && distance < radius) // Wall within line of sight
                    {
                        blocked = true;
                        ShadowCastLimited(distance + 1, start, leftSlope, xx, xy, yx, yy, radius, startX, startY,
                            lightMap, fovSet, map, distanceStrategy, angle, span);
                        newStart = rightSlope;
                    }
                }
            }
        }
    }
}
