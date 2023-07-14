﻿using MassSpectrometry;
using Proteomics;
using Readers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using UsefulProteomicsDatabases;
using Readers;

namespace EngineLayer
{
    public class MMGPTMD
    {
        public static void UpdateTheFilteredPsmFile(MsDataFile msDataFile, string psmFilePath)
        {
            List<FilteredPsmTSV> psms = ReadFilteredPsmTSV(psmFilePath);

            MsDataScan[] dataScans = msDataFile.GetMsDataScans();

            //Use list instead of Scans GEScansList()
            for (int i = 0; i < psms.Count(); i++)
            {
                psms[i].PrecursorScanNumber = dataScans[i].OneBasedPrecursorScanNumber.ToString();
                psms[i].ScanNumber = dataScans[i+1].OneBasedScanNumber.ToString();
            }
            WriteUpdatedFilteredPsmsToTSV(psms, psmFilePath);
        }

        public static void WriteUpdatedFilteredPsmsToTSV(List<FilteredPsmTSV> filteredPsms, string path)
        {
            using (var writer = new StreamWriter(path))
            {
                //makes this an enum?
                string header = "File Name\tScan Number\tPrecursor Scan Number\tScore\tBase Sequence\t" +
                                "Full Sequence\tMods\tProtein Accession\t " +
                                "Protein Name\tGene Name\tOrganism Name\t" +
                                "Start and End Residues in Protein\t" +
                                "Matched Ion Series\tMatched Ion Counts";

                writer.WriteLine(header);
                foreach (var psm in filteredPsms)
                {
                    string[] row = new[]
                    {
                        psm.FileName,
                        psm.ScanNumber,
                        psm.PrecursorScanNumber,
                        psm.Score,
                        psm.BaseSeq,
                        psm.FullSeq,
                        psm.Mods,
                        psm.ProteinAccession,
                        psm.ProteinName,
                        psm.GeneName,
                        psm.OrganismName,
                        psm.StartAndEndResiduesInProtein,
                        psm.MatchedIonSeries,
                        psm.MatchedIonCounts
                    };
                    writer.WriteLine(string.Join('\t', row));
                }
            }
        }

        public static void CreateMzML(MsDataScan[] scans, SourceFile sourceFile, string path)
        {
            SourceFile sourceFile1 = new("no nativeID format", "mzML format",
                null, null, filePath: @"K:\08-30-22_bottomup\fractionated_search\Task3-SearchTask\file_example.mzML", null);

            MsDataFile genericFile = new GenericMsDataFile(scans: scans, sourceFile: sourceFile);

            MzmlMethods.CreateAndWriteMyMzmlWithCalibratedSpectra(genericFile, path, true);
        }

        public static (MsDataScan[], MsDataFile) ExtractScansAndSourceFile(List<FilteredPsmTSV> psms, List<string> filePaths)
        {
            List<MsDataFile> loadedFiles = new();

            foreach (var file in filePaths)
            {
                loadedFiles.Add(Readers.ThermoRawFileReader.LoadAllStaticData(file));
            }

            List<string> fileName = new();

            foreach (var name in loadedFiles)
            {
                fileName.Add(name.FilePath);
            }

            List<Tuple<string, MsDataFile>> tupleList = new();

            MsDataFile dataFile = loadedFiles[0];

            for (int i = 0; i < loadedFiles.Count(); i++)
            {
                tupleList.Add(new Tuple<string, MsDataFile>(item1: fileName[i], item2: loadedFiles[i]));
            }

            var dict = tupleList.ToImmutableDictionary(x => x.Item1, x => x.Item2);

            List<MsDataScan> scanList = new List<MsDataScan>();
            int counter = 1;
            int precursorNumber = 1;
            double retentionTime = 1;
            double injectionTime = 1;
            foreach (var psm in psms)
            {
                foreach (var file in dict)
                {
                    if (file.Key.Contains(psm.FileName))
                    {
                        var ms1 = file.Value.GetMS1Scans().First();

                        MsDataScan ms2 = file.Value.GetOneBasedScan(int.Parse(psm.ScanNumber));
                        double[,] mzIntensitiesMS1 = new double[2, ms1.MassSpectrum.XArray.Length];
                        double[,] mzIntensitiesMS2 = new double[2, ms2.MassSpectrum.XArray.Length];

                        // Two 1-D array to One 2-D array
                        for (int i = 0; i < ms1.MassSpectrum.XArray.Length; i++)
                        {
                            mzIntensitiesMS1[0, i] = ms1.MassSpectrum.XArray[i];
                            mzIntensitiesMS1[1, i] = ms1.MassSpectrum.YArray[i];
                        }
                        for (int i = 0; i < ms2.MassSpectrum.XArray.Length; i++)
                        {
                            mzIntensitiesMS2[0, i] = ms1.MassSpectrum.XArray[i];
                            mzIntensitiesMS2[1, i] = ms1.MassSpectrum.YArray[i];
                        }

                        MzSpectrum spectrumMS1 = new MzSpectrum(mzIntensitiesMS1);
                        MzSpectrum spectrumMS2 = new MzSpectrum(mzIntensitiesMS2);
                        //Recreate Scan
                        MsDataScan scanMS1 = new MsDataScan(massSpectrum: spectrumMS1, oneBasedScanNumber: counter, msnOrder: ms1.MsnOrder, isCentroid: ms1.IsCentroid, polarity: ms1.Polarity,
                            retentionTime: retentionTime, scanWindowRange: ms1.ScanWindowRange, scanFilter: ms1.ScanFilter, mzAnalyzer: ms1.MzAnalyzer,
                            totalIonCurrent: ms1.TotalIonCurrent, injectionTime: injectionTime, noiseData: ms1.NoiseData,
                            nativeId: "controllerType=0 controllerNumber=1 scan=" + counter.ToString(), selectedIonMz: ms1.SelectedIonMZ,
                            selectedIonChargeStateGuess: ms1.SelectedIonChargeStateGuess, selectedIonIntensity: ms1.SelectedIonIntensity,
                            isolationMZ: ms1.IsolationMz, isolationWidth: ms1.IsolationWidth, dissociationType: ms1.DissociationType, oneBasedPrecursorScanNumber: precursorNumber,
                            hcdEnergy: ms1.HcdEnergy);

                        counter++;
                        precursorNumber++;
                        retentionTime++;
                        injectionTime++;

                        MsDataScan scanMS2 = new MsDataScan(massSpectrum: spectrumMS2, oneBasedScanNumber: counter, msnOrder: ms2.MsnOrder, isCentroid: ms2.IsCentroid, polarity: ms2.Polarity,
                        retentionTime: retentionTime, scanWindowRange: ms2.ScanWindowRange, scanFilter: ms2.ScanFilter, mzAnalyzer: ms2.MzAnalyzer,
                        totalIonCurrent: ms2.TotalIonCurrent, injectionTime: injectionTime, noiseData: ms2.NoiseData,
                        nativeId: "controllerType=0 controllerNumber=1 scan=" + counter.ToString(), selectedIonMz: ms2.SelectedIonMZ,
                        selectedIonChargeStateGuess: ms2.SelectedIonChargeStateGuess, selectedIonIntensity: ms2.SelectedIonIntensity,
                        isolationMZ: ms2.IsolationMz, isolationWidth: ms2.IsolationWidth, dissociationType: ms2.DissociationType, oneBasedPrecursorScanNumber: precursorNumber,
                        hcdEnergy: ms2.HcdEnergy);


                        counter++;
                        precursorNumber++;
                        retentionTime++;
                        injectionTime++;
                        //tempFile.SetOneBasedScanNumber(counter);

                        scanList.Add(scanMS1);
                        scanList.Add(scanMS2);
                        break;
                    }
                }
            }

            MsDataScan[] scansArray = scanList.Select(x => x).ToArray();

            return (scansArray, dataFile);
        }

        public static void WriteFastaDBFromFilteredPsm(List<FilteredPsmTSV> psms, string path)
        {
            List<Protein> proteinList = new List<Protein>();

            foreach (var protein in psms)
            {
                proteinList.Add(new Protein(
                    sequence: protein.BaseSeq,
                    accession: protein.ProteinAccession,
                    name: protein.ProteinName,
                    organism: protein.OrganismName
                ));
            }
            ProteinDbWriter.WriteFastaDatabase(proteinList, path, "");
        }


        public static IEnumerable<PsmFromTsv> FilterPsm(List<PsmFromTsv> psms)
        {
            IEnumerable<PsmFromTsv> filteredPsms =
                from psm in psms
                where psm.QValue <= 0.00001 && psm.PEP <= 0.00001 && psm.DecoyContamTarget.Equals("T") &&
                      PsmFromTsv.ParseModifications(psm.FullSequence).Count() >= 2
                select psm;

            return filteredPsms;
        }


        public static void WriteFilteredPsmsToTSV(List<PsmFromTsv> filteredPsms, string path)
        {
            using (var writer = new StreamWriter(path))
            {
                //makes this an enum?
                string header = "File Name\tScan Number\tPrecursor Scan Number\tScore\tBase Sequence\t" +
                                "Full Sequence\tMods\tProtein Accession\t " +
                                "Protein Name\tGene Name\tOrganism Name\t" +
                                "Start and End Residues in Protein\t" +
                                "Matched Ion Series\tMatched Ion Counts";

                writer.WriteLine(header);
                foreach (var psm in filteredPsms)
                {
                    string[] row = new[]
                    {
                        String.Join('-',psm.FileNameWithoutExtension.Split('-').SkipLast(1)),
                        psm.Ms2ScanNumber.ToString(),
                        psm.PrecursorScanNum.ToString(),
                        psm.Score.ToString(),
                        psm.BaseSeq,
                        psm.FullSequence,
                        PsmFromTsv.ParseModifications(psm.FullSequence).Count().ToString(),
                        psm.ProteinAccession,
                        psm.ProteinName,
                        psm.GeneName,
                        psm.OrganismName,
                        psm.StartAndEndResiduesInProtein,
                        string.Join(' ',psm.MatchedIons.Select(x => x.Annotation).ToArray()),
                        psm.MatchedIons.Count().ToString()
                    };
                    writer.WriteLine(string.Join('\t', row));
                }
            }
        }

        public static List<FilteredPsmTSV> ReadFilteredPsmTSV(string path)
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
    }
}
