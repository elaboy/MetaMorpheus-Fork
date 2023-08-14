﻿using Easy.Common.Extensions;
using MassSpectrometry;
using MzLibUtil;
using Nett;
using Proteomics;
using Proteomics.Fragmentation;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using EngineLayer;
using UsefulProteomicsDatabases;
using FlashLFQ;
using System.Text.Json;
using TorchSharp;
using static Nett.TomlObjectFactory;
using static TorchSharp.torch.utils;
using System.Drawing.Imaging;

namespace TaskLayer
{
    public class MultipleSearchEngine
    {
        public List<List<Modification>> CombinationOfModifications { get; set; }
        public List<KeyValuePair<double, Modification[]>> CombinationsWithAddedMass { get; set; }
        public double[] MassArray { get; set; }

        public MultipleSearchEngine(List<Modification> listOfMods, int numberOfVariableMods, bool allCombos)
        {
            List<List<Modification>> comboList = new();
            CombinationsWithAddedMass = new();
            Mc(listOfMods, ref comboList, numberOfVariableMods, allCombos);
            CombinationOfModifications = comboList;
            CombinationsWithAddedMass.Add(CombinationOfModifications.Select(x => new KeyValuePair<double, Modification[]>(
                key: x.Select(x => x.MonoisotopicMass.Value).Sum(), value: x.Select(x => x).ToArray())));
            CombinationsWithAddedMass = CombinationsWithAddedMass.OrderBy(x => x.Key).ToList();
            MassArray = CombinationsWithAddedMass.Select(x => x.Key).ToArray();
        }

        public static (List<DataTable>, List<MultipleSearchResults>) Run(MultipleSearchEngine engine,List<FilteredPsmTSV> psmList, List<Modification> fixedMods,
            MsDataFile dataFile, int maxNumOfMods, string pathToSaveResults)
        {


            List<KeyValuePair<PeptideWithSetModifications, List<MatchedFragmentIon>>> tempMatches = new();


            Parallel.ForEach(psmList, psm =>
            {
                IEnumerable<PeptideWithSetModifications> peptideAsProtein = MultipleSearchEngine.GetPeptideAsProtein(psm, fixedMods);
                var matches = engine.GetPeptideFragmentIonsMatches(psm, dataFile, fixedMods);
                if (matches == null)
                    return;
                tempMatches.Add(matches);
            });

            var results = tempMatches.OrderByDescending(x => x.Value.Count)
                .GroupBy(x => x.Key.BaseSequence).ToList();


            List<DataTable> proteinGroupsTables = new();
            List<MultipleSearchResults> searchResults = new();
            Parallel.ForEach(results, result =>
            {
                var table = new DataTable();
                table.TableName = result.Key;
                foreach (var feature in typeof(MultipleSearchResults).GetProperties())
                {
                    table.Columns.Add(new DataColumn(feature.Name));
                }
                foreach (var peptide in result)
                {
                    var individualPeptide = new MultipleSearchResults()
                    {
                        AccessionNumber = peptide.Key.Protein.Accession,
                        BaseSequence = peptide.Key.Protein.BaseSequence,
                        FullSequence = peptide.Key.FullSequence,
                        IonMatchedCount = peptide.Value.Count,
                        Modifications = String.Join(", ", peptide.Key.AllModsOneIsNterminus.Select(x => $"{x.Value.IdWithMotif}").ToArray()),
                        SequenceCoverage = peptide.Value.Count > 0 ? (peptide.Key.Length*2)/peptide.Value.Count : 0,
                        IsDecoy = peptide.Key.Protein.IsDecoy,
                        MassErrorDa = peptide.Value.Select(x => x.MassErrorDa).ToArray(),
                        MassErrorPpm = peptide.Value.Select(x => x.MassErrorPpm).ToArray(),
                        MatchedIonCharge = peptide.Value.Select(x => x.Charge).ToArray(),
                        MatchedIons = peptide.Value.Select(x => x.Annotation).ToArray(),
                        MatchedMz = peptide.Value.Select(x => x.Mz).ToArray(),
                        TheoricalMz = peptide.Value.Select(x => x.NeutralTheoreticalProduct.NeutralMass).ToArray(),
                        MonoisotopicMass = peptide.Key.MonoisotopicMass,
                        MostAbundantMonoisotopicMass = peptide.Key.MostAbundantMonoisotopicMass,
                        PeptideLength = peptide.Key.Protein.Length
                    };
                    DataRow row = table.NewRow();
                    row[0] = individualPeptide.BaseSequence;
                    row[1] = individualPeptide.SequenceCoverage;
                    row[2] = individualPeptide.IonMatchedCount;
                    row[3] = individualPeptide.Modifications;
                    row[4] = individualPeptide.FullSequence;
                    row[5] = individualPeptide.AccessionNumber;
                    row[6] = individualPeptide.PeptideLength;
                    row[7] = individualPeptide.MonoisotopicMass;
                    row[8] = individualPeptide.MostAbundantMonoisotopicMass;
                    row[9] = individualPeptide.IsDecoy;
                    row[10] = String.Join(", ", individualPeptide.MatchedIons);
                    row[11] = String.Join(", ", individualPeptide.MatchedIonCharge);
                    row[12] = String.Join(", ", individualPeptide.TheoricalMz);
                    row[13] = String.Join(", ", individualPeptide.MatchedMz);
                    row[14] = String.Join(", ", individualPeptide.MassErrorPpm);
                    row[15] = String.Join(", ", individualPeptide.MassErrorDa);

                    table.Rows.Add(row);
                    searchResults.Add(individualPeptide);

                }
                proteinGroupsTables.Add(table);
            });

            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(searchResults.Select(x => x), options);
            File.WriteAllText($@"{pathToSaveResults}", jsonString);

            return (proteinGroupsTables, searchResults);
            //proteinGroups.ItemsSource = proteinGroupsTables.AsEnumerable();
        }

        private static KeyValuePair<double, Modification[]>[] SortAndEliminateDuplicates(KeyValuePair<double, Modification[]>[] combinationsFromDatabase)
        {
            Parallel.ForEach(combinationsFromDatabase, combo =>
            {
                combo = new KeyValuePair<double, Modification[]>(combo.Key,
                    combo.Value.OrderBy(mod => mod.IdWithMotif).ToArray());
            });
            return combinationsFromDatabase.Distinct().OrderBy(x => x.Key).ToArray();
        }

        public List<KeyValuePair<PeptideWithSetModifications, List<MatchedFragmentIon>>> GetPeptideFragmentIonsMatches(FilteredPsmTSV psm, MsDataFile dataFile,
            List<Modification> fixedMods, int cycles=4)
        {

            List<KeyValuePair<PeptideWithSetModifications, List<MatchedFragmentIon>>> results = new();


            var spectrum = dataFile.GetOneBasedScan(int.Parse(psm.ScanNumber));
            //var neutralMass = spectrum.SelectedIonMZ.Value.ToMass(spectrum.SelectedIonChargeStateGuess.Value);

            var peptide = GetPeptideWithMods(psm, fixedMods);

            var deltaMass = GetDeltaMass(psm, peptide.First());
            results.Add(RecursiveSearch(psm, peptide, spectrum, fixedMods, dataFile, deltaMass));

            for(int i = 0; i < cycles; i++)
            {
                results.Add(RecursiveSearch(psm, results.Select(x => x.Key),
                    spectrum, fixedMods, dataFile, deltaMass));
            }

            return results;
        }

        private List<KeyValuePair<PeptideWithSetModifications, List<MatchedFragmentIon>>> RecursiveSearch(FilteredPsmTSV psm,
            IEnumerable<PeptideWithSetModifications> peptide,
            MsDataScan spectrum, List<Modification> fixedMods, MsDataFile dataFile, double deltaMass)
        {
            List<KeyValuePair<PeptideWithSetModifications, List<MatchedFragmentIon>>> results = new();

            foreach (var item in peptide)
            {
                deltaMass = GetDeltaMass(psm, item);
                var probableMods =
                    GetCombinationsThatFitDelta(deltaMass);

                var products = new List<Product>();
                int id = 0;
                foreach (var mod in probableMods)
                {
                    var ptm = item.Protein.Digest(new DigestionParams("top-down"), fixedMods, mod);
                    //var ptm = GetPeptideWithMods(psm, fixedMods, mod.ToList());
                    foreach (var variant in ptm)
                    {
                        variant.Fragment(spectrum.DissociationType ?? DissociationType.HCD, FragmentationTerminus.Both,
                            products);

                        var match = MetaMorpheusEngine.MatchFragmentIons(new Ms2ScanWithSpecificMass(spectrum,
                            double.Parse(psm.PrecursorMass),
                            spectrum.SelectedIonChargeStateGuess.Value, dataFile.FilePath,
                            new CommonParameters()), products, new CommonParameters());

                        if (results.Count == 0)
                        {
                            results.Add(new KeyValuePair<PeptideWithSetModifications, List<MatchedFragmentIon>>(variant, match));
                            id = id + 1;
                        }
                        else
                        {
                            if (results
                                    .OrderBy(x => x.Value.Count)
                                    .First().Value.Count < match.Count)
                            {
                                results.Add(new KeyValuePair<PeptideWithSetModifications, List<MatchedFragmentIon>>(variant, match));
                                id = id + 1;
                            }
                        }


                    }
                }
            }
            return results;
        }

        /// <summary>
        /// Returns the mass difference between chosen precursor ion and given peptide.
        /// </summary>
        /// <returns></returns>
        private static double GetDeltaMass(FilteredPsmTSV psm, PeptideWithSetModifications peptide)
        {
            //var spectrum = dataFile.GetOneBasedScan(int.Parse(psm.ScanNumber));
            var precursorMass = double.Parse(psm.PrecursorMass);
            //var precursorIonMass = spectrum.SelectedIonMonoisotopicGuessMz.Value * spectrum.SelectedIonChargeStateGuess.Value;

            return precursorMass - peptide.MonoisotopicMass;

        }

        /// <summary>
        /// Returns the Combination of mods that are possible. Uses binary search on the IOrderedEnumerable of mods.
        /// </summary>
        /// <param name="possibleMods"></param>
        /// <param name="peptide"></param>
        /// <param name="psm"></param>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        private List<List<Modification>> GetCombinationsThatFitDelta(double deltaMass)
        {
            var tolerance = new PpmTolerance(15);

            //var massArray = CombinationsFromDatabase.OrderBy(x => x.Key).Select(x => x.Key).ToArray();

            var temp1 = tolerance.GetMaximumValue(deltaMass);
            var temp2 = tolerance.GetMinimumValue(deltaMass);

            var maxIndex = ~Array.BinarySearch(MassArray, tolerance.GetMaximumValue(deltaMass));
            var minIndex = ~Array.BinarySearch(MassArray, tolerance.GetMinimumValue(deltaMass));

            if (maxIndex == minIndex && maxIndex >= CombinationOfModifications.Count || minIndex >= CombinationOfModifications.Count)
                return new List<List<Modification>>();

            int[] rangeIndex = Enumerable.Range(minIndex, Math.Abs(maxIndex - minIndex)).ToArray();

            if (rangeIndex.Length == 0)
                rangeIndex = new[] { maxIndex };

            var rangeOfPossibleMods = rangeIndex.Select(x => CombinationOfModifications[x]);

            return rangeOfPossibleMods.ToList();
        }

        /// <summary>
        /// Returns collection of custom psms. These are part of a custom class for development, not for production.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static List<FilteredPsmTSV> ReadFilteredPsmTSVShort(string path)
        {
            List<FilteredPsmTSV> filteredList = new List<FilteredPsmTSV>();

            using (var reader = new StreamReader(path))
            {
                reader.ReadLine();
                string[] lineCheck;
                while (reader.EndOfStream == false)
                {
                    var line = reader.ReadLine().Split('\t');
                    FilteredPsmTSV filteredPsm = new FilteredPsmTSV(line);
                    filteredList.Add(filteredPsm);
                }
            }

            return filteredList;
        }

        private static IEnumerable<PeptideWithSetModifications> GetPeptideWithMods(FilteredPsmTSV psm, List<Modification> fixedMods = null, List<Modification> variableMods = null)
        {
            var protein = new Protein(psm.BaseSeq, psm.ProteinAccession);
            return protein.Digest(new DigestionParams("top-down"), fixedMods, variableMods);
        }

        /// <summary>
        /// Returns peptide threated as a protein using top-down digestion. Used for GPTMD sequence lead.
        /// </summary>
        /// <param name="psm"></param>
        /// <param name="fixedMods"></param>
        /// <returns></returns>
        public static IEnumerable<PeptideWithSetModifications> GetPeptideAsProtein(FilteredPsmTSV psm, List<Modification> fixedMods = null)
        {
            var peptideProteinDigest =
                new Protein(psm.BaseSeq, psm.ProteinAccession).Digest(new DigestionParams(protease: "top-down"),
                    allKnownFixedModifications: fixedMods,
                    variableModifications: new List<Modification>());

            return peptideProteinDigest;
        }

        /// <summary>
        /// Returns mods from a toml containing common modifications. 
        /// </summary>
        /// <param name="gptmdToml"></param>
        /// <returns></returns>
        public static IEnumerable<Modification> GetModsFromGptmdThing(string gptmdToml = @"Task1-GPTMDTaskconfig.toml")
        {
            var task = Toml.ReadFile<GptmdTask>(gptmdToml,
                MetaMorpheusTask.tomlConfig);

            var mods = GlobalVariables.AllModsKnownDictionary;

            foreach (var (item1, item2) in task.GptmdParameters.ListOfModsGptmd)
            {
                if (mods.TryGetValue(item2, out Modification mod))
                {
                    yield return mod;
                }
            }
        }
        public static void Mc(List<Modification> sortedListOfModsToAdd, ref List<List<Modification>> listOfModCombinations, int maxNumberOfModsInGroup, bool allModsFromOneToN)
        {
            List<List<Modification>> newModLists = new();
            if (maxNumberOfModsInGroup == 0)
            {
                foreach (var modificationToAdd in sortedListOfModsToAdd)
                {
                    newModLists.Add(new List<Modification>() { modificationToAdd });
                }
                newModLists = newModLists.DistinctBy(n => ModListNameString(n)).ToList();
                listOfModCombinations = newModLists;
            }
            else
            {
                Mc(sortedListOfModsToAdd, ref listOfModCombinations, maxNumberOfModsInGroup - 1, allModsFromOneToN);
                newModLists.Clear();
                foreach (var modList in listOfModCombinations.Where(c => c.Count == (maxNumberOfModsInGroup - 1)))
                {
                    foreach (var modificationToAdd in sortedListOfModsToAdd)
                    {
                        List<Modification> newModList = modList.ToList();
                        newModList.Add(modificationToAdd);
                        newModLists.Add(newModList.OrderBy(n => n.IdWithMotif).ToList());
                    }
                }
                newModLists = newModLists.DistinctBy(n => ModListNameString(n)).ToList();
                listOfModCombinations.AddRange(newModLists);
                if (!allModsFromOneToN)
                {
                    listOfModCombinations = listOfModCombinations.Where(c => c.Count == maxNumberOfModsInGroup).ToList();
                }
            }

        }
        public static string ModListNameString(List<Modification> list)
        {
            return String.Join("", list.Select(n => n.IdWithMotif));
        }
    }
}
