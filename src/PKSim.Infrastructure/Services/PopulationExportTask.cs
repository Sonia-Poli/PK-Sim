using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using PKSim.Assets;
using OSPSuite.Utility;
using OSPSuite.Utility.Extensions;
using PKSim.Core;
using PKSim.Core.Mappers;
using PKSim.Core.Model;
using PKSim.Core.Services;
using PKSim.Presentation.Core;
using PKSim.Presentation.Presenters;
using OSPSuite.Core.Domain;
using OSPSuite.Core.Domain.Formulas;
using OSPSuite.Core.Domain.Mappers;
using OSPSuite.Core.Domain.Services;
using OSPSuite.Core.Extensions;
using OSPSuite.Core.Serialization.SimModel.Services;
using OSPSuite.Core.Services;
using OSPSuite.Presentation.Core;
using ILazyLoadTask = PKSim.Core.Services.ILazyLoadTask;

namespace PKSim.Infrastructure.Services
{
   public class PopulationExportTask : IPopulationExportTask
   {
      private readonly IApplicationController _applicationController;
      private readonly IEntityPathResolver _entityPathResolver;
      private readonly ILazyLoadTask _lazyLoadTask;
      private readonly ISimModelExporter _simModelExporter;
      private readonly ISimulationToModelCoreSimulationMapper _modelCoreSimulationMapper;
      private readonly IWorkspace _workspace;
      private readonly IPKSimConfiguration _configuration;

      private readonly ISimulationSettingsRetriever _simulationSettingsRetriever;
      private readonly IDialogCreator _dialogCreator;
      private readonly ICloner _cloner;

      public PopulationExportTask(IApplicationController applicationController, IEntityPathResolver entityPathResolver,
         ILazyLoadTask lazyLoadTask, ISimModelExporter simModelExporter, ISimulationToModelCoreSimulationMapper modelCoreSimulationMapper,
         IWorkspace workspace, IPKSimConfiguration configuration, ISimulationSettingsRetriever simulationSettingsRetriever,
         IDialogCreator dialogCreator, ICloner cloner)
      {
         _applicationController = applicationController;
         _entityPathResolver = entityPathResolver;
         _lazyLoadTask = lazyLoadTask;
         _simModelExporter = simModelExporter;
         _modelCoreSimulationMapper = modelCoreSimulationMapper;
         _workspace = workspace;
         _configuration = configuration;

         _simulationSettingsRetriever = simulationSettingsRetriever;
         _dialogCreator = dialogCreator;
         _cloner = cloner;
      }

      public void ExportToCSV(Population population)
      {
         exportVectorialParametersContainerToCSV(population, CreatePopulationDataFor);
      }

      public void ExportToCSV(PopulationSimulation populationSimulation)
      {
         exportVectorialParametersContainerToCSV(populationSimulation, CreatePopulationDataFor);
      }

      private void exportVectorialParametersContainerToCSV<T>(T advancedParameterContainer, Func<T, DataTable> createData) where T : IAdvancedParameterContainer
      {
         using (var presenter = _applicationController.Start<ISelectFilePresenter>())
         {
            var populationFile = presenter.SelectFile(PKSimConstants.UI.ExportPopulationToCSVTitle, Constants.Filter.CSV_FILE_FILTER, PKSimConstants.UI.DefaultPopulationExportNameFor(advancedParameterContainer.Name), Constants.DirectoryKey.POPULATION);
            if (populationFile == null)
               return;

            var dataTable = createData(advancedParameterContainer);
            dataTable.ExportToCSV(populationFile.FilePath, comments: getProjectMetaInfo(populationFile.Description));
         }
      }

      private IReadOnlyList<string> getProjectMetaInfo(string description = null)
      {
         var metaInfo = new List<string> {"Project: {0}".FormatWith(_workspace.Project.Name), "{0} version: {1}".FormatWith(CoreConstants.ProductName, _configuration.FullVersion)};
         if (!string.IsNullOrEmpty(description))
            metaInfo.Add(description);

         return metaInfo;
      }

      public DataTable CreatePopulationDataFor(Population population)
      {
         _lazyLoadTask.Load(population);
         var dataTable = new DataTable(population.Name);
         dataTable.BeginLoadData();

         //Create one column for the parameter path
         addCovariates(population, dataTable);

         //add advanced parameters
         var allAdvancedParameters = population.AllAdvancedParameters(_entityPathResolver).ToList();
         var parametersToExport = population.AllVectorialParameters(_entityPathResolver);

         //do not take the one that should never be exported
         parametersToExport = parametersToExport.Where(p => parameterShouldBeExported(p, allAdvancedParameters));

         parametersToExport.Each(p => addParameterToTable(population, dataTable, p));
         dataTable.EndLoadData();
         return dataTable;
      }

      private void addCovariates(Population population, DataTable dataTable)
      {
         var individualIds = Enumerable.Range(0, population.NumberOfItems).ToList();

         //add individual ids column
         individualIds.Each(i => dataTable.Rows.Add(dataTable.NewRow()));
         addColumnValues(population, dataTable, Constants.Population.INDIVIDUAL_ID_COLUMN, individualIds);

         //and one column for each individual in the population
         foreach (var covariate in population.AllCovariateNames())
         {
            if (covariate == CoreConstants.Covariates.GENDER)
               addColumnValues(population, dataTable, CoreConstants.Parameter.GENDER, population.AllGenders().Select(x => x.Index).ToList());
            else if (covariate == CoreConstants.Covariates.RACE)
               addColumnValues(population, dataTable, CoreConstants.Parameter.RACE_INDEX, population.AllRaces().Select(x => x.RaceIndex).ToList());
            else
               addColumnValues(population, dataTable, covariate, population.AllCovariateValuesFor(covariate));
         }
      }

      private void addColumnValues<T>(IVectorialParametersContainer parameterContainer, DataTable dataTable, string columnName, IReadOnlyList<T> allValues)
      {
         dataTable.AddColumn<T>(columnName);
         for (int i = 0; i < parameterContainer.NumberOfItems; i++)
         {
            dataTable.Rows[i][columnName] = allValues[i];
         }
      }

      private bool parameterShouldBeExported(IParameter parameter, IEnumerable<IParameter> advancedParameters)
      {
         //BMI and BodyWeight should always be exported (Mantis 2907)
         if (parameter.NameIsOneOf(CoreConstants.Parameter.BMI, CoreConstants.Parameter.WEIGHT)) return true;

         //BMI MeanHeight MeanWeight should never be exported (Mantis 2907)
         if (parameter.NameIsOneOf(CoreConstants.Parameter.MEAN_WEIGHT, CoreConstants.Parameter.MEAN_HEIGHT)) return false;

         //distribution parameter search as mean, std, gsd etc should not be exported
         if (CoreConstants.Parameter.AllDistributionParameters.Contains(parameter.Name)) return false;

         //advanced parameters should always be exported
         if (advancedParameters.Contains(parameter))
            return true;

         return !parameter.Formula.IsExplicit();
      }

      public DataTable CreatePopulationDataFor(PopulationSimulation populationSimulation)
      {
         _lazyLoadTask.Load(populationSimulation);
         var population = populationSimulation.Population;
         //retrieve table for population
         var dataTable = CreatePopulationDataFor(population);

         //add advanced parameters
         populationSimulation.AllAdvancedParameters(_entityPathResolver).Each(p => addParameterToTable(populationSimulation, dataTable, p));

         return dataTable;
      }

      public void ExportForCluster(PopulationSimulation populationSimulation)
      {
         _lazyLoadTask.Load(populationSimulation);

         if (settingsRequired(populationSimulation))
         {
            var outputSelections = _simulationSettingsRetriever.SettingsFor(populationSimulation);
            if (outputSelections == null)
               return;

            populationSimulation.OutputSelections.UpdatePropertiesFrom(outputSelections, _cloner);
         }

         FileSelection populationExport;
         using (var presenter = _applicationController.Start<ISelectFilePresenter>())
         {
            populationExport = presenter.SelectDirectory(PKSimConstants.UI.ExportForClusterSimulationTitle, Constants.DirectoryKey.SIM_MODEL_XML);
         }
         if (populationExport == null)
            return;

         var populationFolder = populationExport.FilePath;
         var existingFiles = Directory.GetFiles(populationFolder);
         if (existingFiles.Any())
         {
            if (_dialogCreator.MessageBoxYesNo(PKSimConstants.UI.DeleteFilesIn(populationFolder)).Equals(ViewResult.No))
               return;

            existingFiles.Each(FileHelper.DeleteFile);
         }

         var fileName = populationSimulation.Name;
         var modelFileFullPath = Path.Combine(populationFolder, $"{fileName}.xml");
         var agingFileFullPath = Path.Combine(populationFolder, $"{fileName}{CoreConstants.Population.TableParameterExport}.csv");
         var outputDeffinitionFileFullPath = Path.Combine(populationFolder, $"{fileName}{CoreConstants.Population.OutputDefinitionExport}.csv");

         //Model
         _simModelExporter.Export(_modelCoreSimulationMapper.MapFrom(populationSimulation, shouldCloneModel: false), modelFileFullPath);
         // Outputs

         var outputSelection = populationSimulation.OutputSelections;

         exportOutputDefiniton(outputSelection, outputDeffinitionFileFullPath);

         //all values
         var dataTable = CreatePopulationDataFor(populationSimulation);
         dataTable.ExportToCSV(Path.Combine(populationFolder, $"{fileName}.csv"), comments: getProjectMetaInfo(populationExport.Description));

         //all aging data
         var agingData = populationSimulation.AgingData.ToDataTable();
         if (agingData.Rows.Count > 0)
            agingData.ExportToCSV(agingFileFullPath, comments: getProjectMetaInfo(populationExport.Description));
      }

      private void exportOutputDefiniton(OutputSelections outputSelections, string outputDeffinitionFileFullPath)
      {
         using (var sw = new StreamWriter(outputDeffinitionFileFullPath, false))
         {
            // Add Simulation Name to paths for sim model
            outputSelections.AllOutputs.Each(sq => sw.WriteLine("{0}{1}", sq.Path, ';'));
            sw.Close();
         }
      }

      private bool settingsRequired(Simulation simulation)
      {
         if (simulation.OutputSelections == null)
            return true;

         return !simulation.OutputSelections.HasSelection;
      }

      private void addParameterToTable(IVectorialParametersContainer parameterContainer, DataTable dataTable, IParameter parameter)
      {
         //some path have changed and the parameter is not found anymore
         if (parameter == null) return;

         addColumnForParameterToTable(parameterContainer, dataTable, _entityPathResolver.PathFor(parameter));
      }

      private void addColumnForParameterToTable(IVectorialParametersContainer parameterContainer, DataTable dataTable, string parameterPath)
      {
         addColumnValues(parameterContainer, dataTable, parameterPath, parameterContainer.AllValuesFor(parameterPath));
      }
   }
}