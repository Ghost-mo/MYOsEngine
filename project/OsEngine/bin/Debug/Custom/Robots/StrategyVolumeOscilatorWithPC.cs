﻿using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System.Linq;
using OsEngine.Logging;

/* Description
trading robot for osengine

The trend robot on Strategy VolumeOscilator With PriceChannel.

Buy:
1. The candle closed above the upper PC line.
2. The Volume Oscillator indicator line is above 0.

Sell:
1. The candle closed below the lower PC line.
2. The Volume Oscillator indicator line is below 0.

Exit from buy: The trailing stop is placed at the minimum for the period specified 
for the trailing stop and is transferred, (slides), to new price lows, also for the specified period.

Exit from sell: The trailing stop is placed at the maximum for the period specified 
for the trailing stop and is transferred (slides) to the new maximum of the price, also for the specified period.
 */


namespace OsEngine.Robots.AO
{
    [Bot("StrategyVolumeOscilatorWithPC")] // We create an attribute so that we don't write anything to the BotFactory
    public class StrategyVolumeOscilatorWithPC : BotPanel
    {
        private BotTabSimple _tab;

        // Basic Settings
        private StrategyParameterString Regime;
        private StrategyParameterString VolumeRegime;
        private StrategyParameterDecimal VolumeOnPosition;
        private StrategyParameterDecimal Slippage;
        private StrategyParameterTimeOfDay StartTradeTime;
        private StrategyParameterTimeOfDay EndTradeTime;

        // Indicator setting 
        private StrategyParameterInt LenghtVolOsSlow;
        private StrategyParameterInt LenghtVolOsFast;
        private StrategyParameterInt PcUpLength;
        private StrategyParameterInt PcDownLength;

        // Indicator
        Aindicator _VolumeOsc;
        Aindicator _PC;

        // The last value of the indicator
        private decimal _lastVolume;

        // The prev value of the indicator
        private decimal _prevUpPC;
        private decimal _prevDownPC;

        // Exit
        private StrategyParameterInt TrailCandlesLong;
        private StrategyParameterInt TrailCandlesShort;

        public StrategyVolumeOscilatorWithPC(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            // Basic setting
            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency" }, "Base");
            VolumeOnPosition = CreateParameter("Volume", 1, 1.0m, 50, 4, "Base");
            Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");
            StartTradeTime = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
            EndTradeTime = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

            // Indicator setting
            PcUpLength = CreateParameter("Up Line Length", 21, 7, 48, 7, "Indicator");
            PcDownLength = CreateParameter("Down Line Length", 21, 7, 48, 7, "Indicator");
            LenghtVolOsSlow = CreateParameter("LenghtVolOsSlow", 10, 20, 300, 1, "Indicator");
            LenghtVolOsFast = CreateParameter("LenghtVolOsFast", 10, 10, 300, 1, "Indicator");

            // Create indicator VoluneOscilator
            _VolumeOsc = IndicatorsFactory.CreateIndicatorByName("VolumeOscilator", name + "VolumeOscilator", false);
            _VolumeOsc = (Aindicator)_tab.CreateCandleIndicator(_VolumeOsc, "NewArea1");
            ((IndicatorParameterInt)_VolumeOsc.Parameters[0]).ValueInt = LenghtVolOsSlow.ValueInt;
            ((IndicatorParameterInt)_VolumeOsc.Parameters[1]).ValueInt = LenghtVolOsFast.ValueInt;
            _VolumeOsc.Save();

            // Create indicator PC
            _PC = IndicatorsFactory.CreateIndicatorByName("PriceChannel", name + "PC", false);
            _PC = (Aindicator)_tab.CreateCandleIndicator(_PC, "Prime");
            ((IndicatorParameterInt)_PC.Parameters[0]).ValueInt = PcUpLength.ValueInt;
            ((IndicatorParameterInt)_PC.Parameters[1]).ValueInt = PcDownLength.ValueInt;
            _PC.Save();

            // Exit
            TrailCandlesLong = CreateParameter("Trail Candles Long", 5, 5, 200, 5, "Exit");
            TrailCandlesShort = CreateParameter("Trail Candles Short", 5, 5, 200, 5, "Exit");

            // Subscribe to the indicator update event
            ParametrsChangeByUser += StrategyVolumeOscilatorWithPC_ParametrsChangeByUser;

            // Subscribe to the candle finished event
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;

            Description = "The trend robot on Strategy VolumeOscilator With PriceChannel. " +
                "Buy: " +
                "1. The candle closed above the upper PC line. " +
                "2. The Volume Oscillator indicator line is above 0. " +
                "Sell: " +
                "1. The candle closed below the lower PC line. " +
                "2. The Volume Oscillator indicator line is below 0. " +
                "Exit from buy: The trailing stop is placed at the minimum for the period specified  " +
                "for the trailing stop and is transferred, (slides), to new price lows, also for the specified period. " +
                "Exit from sell: The trailing stop is placed at the maximum for the period specified  " +
                "for the trailing stop and is transferred (slides) to the new maximum of the price, also for the specified period.";
        }

        private void StrategyVolumeOscilatorWithPC_ParametrsChangeByUser()
        {
            ((IndicatorParameterInt)_VolumeOsc.Parameters[0]).ValueInt = LenghtVolOsSlow.ValueInt;
            ((IndicatorParameterInt)_VolumeOsc.Parameters[1]).ValueInt = LenghtVolOsFast.ValueInt;
            _VolumeOsc.Save();
            _VolumeOsc.Reload();
            ((IndicatorParameterInt)_PC.Parameters[0]).ValueInt = PcUpLength.ValueInt;
            ((IndicatorParameterInt)_PC.Parameters[1]).ValueInt = PcDownLength.ValueInt;
            _PC.Save();
            _PC.Reload();
        }

        // The name of the robot in OsEngine
        public override string GetNameStrategyType()
        {
            return "StrategyVolumeOscilatorWithPC";
        }
        public override void ShowIndividualSettingsDialog()
        {

        }

        // Candle Finished Event
        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            // If the robot is turned off, exit the event handler
            if (Regime.ValueString == "Off")
            {
                return;
            }

            // If there are not enough candles to build an indicator, we exit
            if (candles.Count < PcUpLength.ValueInt ||
                candles.Count < PcDownLength.ValueInt ||
                candles.Count < LenghtVolOsSlow.ValueInt ||
                candles.Count < LenghtVolOsFast.ValueInt)
            {
                return;
            }

            // If the time does not match, we leave
            if (StartTradeTime.Value > _tab.TimeServerCurrent ||
                EndTradeTime.Value < _tab.TimeServerCurrent)
            {
                return;
            }

            List<Position> openPositions = _tab.PositionsOpenAll;

            // If there are positions, then go to the position closing method
            if (openPositions != null && openPositions.Count != 0)
            {
                LogicClosePosition(candles);
            }

            // If the position closing mode, then exit the method
            if (Regime.ValueString == "OnlyClosePosition")
            {
                return;
            }
            // If there are no positions, then go to the position opening method
            if (openPositions == null || openPositions.Count == 0)
            {
                LogicOpenPosition(candles);
            }
        }

        // Opening logic
        private void LogicOpenPosition(List<Candle> candles)
        {
            // The last value of the indicators               
            _lastVolume = _VolumeOsc.DataSeries[0].Last;

            // The prev value of the indicator
            _prevUpPC = _PC.DataSeries[0].Values[_PC.DataSeries[0].Values.Count - 2];
            _prevDownPC = _PC.DataSeries[1].Values[_PC.DataSeries[1].Values.Count - 2];

            List<Position> openPositions = _tab.PositionsOpenAll;

            if (openPositions == null || openPositions.Count == 0)
            {
                decimal lastPrice = candles[candles.Count - 1].Close;

                // Slippage
                decimal _slippage = Slippage.ValueDecimal * _tab.Securiti.PriceStep;

                // Long
                if (Regime.ValueString != "OnlyShort") // If the mode is not only short, then we enter long
                {
                    if (lastPrice > _prevUpPC && _lastVolume > 0)
                    {
                        _tab.BuyAtLimit(GetVolume(), _tab.PriceBestAsk + _slippage);
                    }
                }

                // Short
                if (Regime.ValueString != "OnlyLong") // If the mode is not only long, then we enter short
                {
                    if (lastPrice < _prevDownPC && _lastVolume < 0)
                    {
                        _tab.SellAtLimit(GetVolume(), _tab.PriceBestBid - _slippage);
                    }
                }
            }
        }

        // Logic close position
        private void LogicClosePosition(List<Candle> candles)
        {
            List<Position> openPositions = _tab.PositionsOpenAll;

            decimal _slippage = Slippage.ValueDecimal * _tab.Securiti.PriceStep;

            decimal lastPrice = candles[candles.Count - 1].Close;

            for (int i = 0; openPositions != null && i < openPositions.Count; i++)
            {
                Position positions = openPositions[i];

                if (positions.State != PositionStateType.Open)
                {
                    continue;
                }

                if (positions.Direction == Side.Buy) // If the direction of the position is purchase
                {
                    decimal price = GetPriceStop(Side.Buy, candles, candles.Count - 1);
                    if (price == 0)
                    {
                        return;
                    }
                    _tab.CloseAtTrailingStop(openPositions[0], price, price - _slippage);
                }
                else // If the direction of the position is sale
                {
                    decimal price = GetPriceStop(Side.Sell, candles, candles.Count - 1);
                    if (price == 0)
                    {
                        return;
                    }
                    _tab.CloseAtTrailingStop(openPositions[0], price, price + _slippage);
                }
            }
        }

        private decimal GetPriceStop(Side side, List<Candle> candles, int index)
        {
            if (candles == null || index < TrailCandlesLong.ValueInt || index < TrailCandlesShort.ValueInt)
            {
                return 0;
            }

            if (side == Side.Buy)
            {
                decimal price = decimal.MaxValue;

                for (int i = index; i > index - TrailCandlesLong.ValueInt; i--)
                {
                    if (candles[i].Low < price)
                    {
                        price = candles[i].Low;
                    }
                }
                return price;
            }

            if (side == Side.Sell)
            {
                decimal price = 0;

                for (int i = index; i > index - TrailCandlesShort.ValueInt; i--)
                {
                    if (candles[i].High > price)
                    {
                        price = candles[i].High;
                    }
                }

                return price;
            }
            return 0;
        }

        // Method for calculating the volume of entry into a position
        private decimal GetVolume()
        {
            decimal volume = 0;

            if (VolumeRegime.ValueString == "Contract currency")
            {
                decimal contractPrice = _tab.PriceBestAsk;
                volume = VolumeOnPosition.ValueDecimal / contractPrice;
            }
            else if (VolumeRegime.ValueString == "Number of contracts")
            {
                volume = VolumeOnPosition.ValueDecimal;
            }

            // If the robot is running in the tester
            if (StartProgram == StartProgram.IsTester)
            {
                volume = Math.Round(volume, 6);
            }
            else
            {
                volume = Math.Round(volume, _tab.Securiti.DecimalsVolume);
            }
            return volume;
        }
    }
}
