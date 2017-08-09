﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.Statistics;
using MathNet.Numerics;
using MathNet.Numerics.Distributions;
using System.Runtime.CompilerServices;
using GeneticSharp.Domain.Chromosomes;

namespace Optimization
{

    /// <summary>
    /// Deflated sharpe ratio fitness.
    /// </summary>
    /// <remarks>Calculates fitness by adjusting for the expectation that rate of false positives will increase with number of tests. Implements algorithm detailed here: http://www.davidhbailey.com/dhbpapers/deflated-sharpe.pdf </remarks>
    public class DeflatedSharpeRatioFitness : OptimizerFitness
    {

        #region Declarations
        protected Dictionary<string, double> SharpeData { get; set; }
        protected Dictionary<string, double> ReturnsData { get; set; }
        protected double N { get; set; } //number of trials
        protected double V { get; set; } //variance of results
        protected double T { get; set; } //sample length
        protected double Skewness { get; set; }
        protected double Kurtosis { get; set; }
        protected double SharpeRatio { get; set; }
        #endregion

        public DeflatedSharpeRatioFitness(IOptimizerConfiguration config) : base(config)
        {
        }

        public virtual void Initialize()
        {
            var fullResults = OptimizerAppDomainManager.GetResults();
            //let's exclude non-trading tests and any marked as error/failure with -10 Sharpe
            var hasTraded = fullResults.Where(d => d.Value["TotalNumberOfTrades"] != 0 && d.Value["SharpeRatio"] > -10);
            SharpeData = hasTraded.ToDictionary(k => k.Key, v => (double)v.Value["SharpeRatio"]);
            ReturnsData = hasTraded.ToDictionary(k => k.Key, v => (double)v.Value["CompoundingAnnualReturn"]);

            N = SharpeData.Count();
            var statistics = new DescriptiveStatistics(ReturnsData.Select(d => d.Value));
            V = statistics.Variance;
            T = (Config.EndDate - Config.StartDate).Value.TotalDays;
            Skewness = statistics.Skewness;
            Kurtosis = statistics.Kurtosis;
            SharpeRatio = SharpeData.Any() ? SharpeData.Max(d => d.Value) : 0;
        }

        //cumulative standard normal distribution
        private double Z(double x)
        {
            return new Normal(0, 1).CumulativeDistribution(x);
        }

        //cumulative standard normal distribution inverse
        private double ZInverse(double x)
        {
            return new Normal(0, 1).InverseCumulativeDistribution(x);
        }

        public double CalculateExpectedMaximum()
        {
            var result = Math.Sqrt(1 / V) * ((1 - Constants.EulerMascheroni) * ZInverse(1 - 1 / N) + Constants.EulerMascheroni * ZInverse(1 - 1 / (N * Constants.E)));
            return result;
        }

        public double CalculateDeflatedSharpeRatio(double expectedMaximum)
        {
            var nonAnnualized = (SharpeRatio / Math.Sqrt(250));
            var top = (nonAnnualized - expectedMaximum) * Math.Sqrt(T - 1);
            var bottom = Math.Sqrt(1 - (Skewness) * nonAnnualized + ((Kurtosis - 1) / 4) * Math.Pow(nonAnnualized, 2));

            return Z(top / bottom);
        }

        protected override FitnessResult CalculateFitness(Dictionary<string, decimal> result)
        {
            Initialize();

            //we've not enough results: abandon attempt
            if (N == 0 || double.IsNaN(Kurtosis))
            {
                return new FitnessResult { Fitness = 0, Value = -10 };
            }

            var fitness = CalculateDeflatedSharpeRatio(CalculateExpectedMaximum());

            return new FitnessResult { Fitness = double.IsNaN(fitness) ? 0 : fitness, Value = result["SharpeRatio"] };
        }

        public override double Evaluate(IChromosome chromosome)
        {
            this.Name = "DeflatedSharpe";

            return base.Evaluate(chromosome);
        }

    }
}
