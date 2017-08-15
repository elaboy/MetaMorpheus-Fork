﻿using System;

namespace EngineLayer
{
    [Serializable]
    internal class CompactPeptideWithModifiedMass : CompactPeptideBase
    {
        #region Public Constructors

        public CompactPeptideWithModifiedMass(CompactPeptideBase cp, double MonoisotopicMassIncludingFixedMods)
        {
            this.CTerminalMasses = cp.CTerminalMasses;
            this.NTerminalMasses = cp.NTerminalMasses;
            this.MonoisotopicMassIncludingFixedMods = cp.MonoisotopicMassIncludingFixedMods;
            this.ModifiedMass = MonoisotopicMassIncludingFixedMods;
        }

        #endregion Public Constructors

        #region Public Properties

        public double ModifiedMass { get; set; }

        #endregion Public Properties

        #region Public Methods

        public void SwapMonoisotopicMassWithModifiedMass()
        {
            double tempDouble = this.MonoisotopicMassIncludingFixedMods;
            this.MonoisotopicMassIncludingFixedMods = this.ModifiedMass;
            this.ModifiedMass = tempDouble;
        }

        #endregion Public Methods
    }
}