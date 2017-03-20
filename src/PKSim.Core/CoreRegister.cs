using PKSim.Core.Batch;
using PKSim.Core.Mappers;
using PKSim.Core.Model;
using PKSim.Core.Services;
using OSPSuite.Core.Commands;
using OSPSuite.Core.Domain;
using OSPSuite.Core.Domain.Services;
using OSPSuite.Core.Domain.Services.ParameterIdentifications;
using OSPSuite.Core.Domain.UnitSystem;
using OSPSuite.Core.Maths.Interpolations;
using OSPSuite.Utility.Collections;
using OSPSuite.Utility.Container;
using OSPSuite.Utility.Data;
using IContainer = OSPSuite.Utility.Container.IContainer;
using Individual = PKSim.Core.Model.Individual;
using Simulation = PKSim.Core.Model.Simulation;

namespace PKSim.Core
{
   public class CoreRegister : Register
   {
      public override void RegisterInContainer(IContainer container)
      {
         container.AddRegister(x => x.FromInstance(new OSPSuite.Core.CoreRegister {RegisterParameter = false}));

         //Register PKSim.Core 
         container.AddScanner(scan =>
         {
            scan.AssemblyContainingType<CoreRegister>();

            //Exclude type that should be register as singleton because of caching 
            scan.ExcludeType<FormulationValuesRetriever>();
            scan.ExcludeType<ObjectTypeResolver>();
            scan.ExcludeType<PKSimDimensionFactory>();
            scan.ExcludeType<PKSimObjectBaseFactory>();
            scan.ExcludeType<DistributionFormulaFactory>();
            scan.ExcludeType<ApplicationSettings>();
            scan.ExcludeType<ProjectChangedNotifier>();
            scan.ExcludeType<BatchLogger>();
            scan.ExcludeType<SimulationRunner>();
            scan.WithConvention<PKSimRegistrationConvention>();
         });

         //Register singletons explicitely
         container.AddScanner(scan =>
         {
            scan.AssemblyContainingType<CoreRegister>();
            scan.IncludeType<FormulationValuesRetriever>();
            scan.IncludeType<ObjectTypeResolver>();
            scan.IncludeType<PKSimObjectBaseFactory>();
            scan.IncludeType<DistributionFormulaFactory>();
            scan.IncludeType<ApplicationSettings>();
            scan.IncludeType<ProjectChangedNotifier>();
            scan.IncludeType<BatchLogger>();
            scan.IncludeType<SimulationRunner>();

            scan.RegisterAs(LifeStyle.Singleton);
            scan.WithConvention<PKSimRegistrationConvention>();
         });

         container.Register<ICoreSimulationFactory, SimulationFactory>();
         container.Register<ISetParameterTask, ParameterTask>();
         container.Register<ITransferOptimizedParametersToSimulationsTask, TransferOptimizedParametersToSimulationsTask<IExecutionContext>>();

         //other singleton external to application
         container.Register<ICloneManager, Cloner>(LifeStyle.Singleton);

         //Register special type for parameters so that core methods in the context of pksim creates a PKSimParameter
         container.Register<IParameter, PKSimParameter>();
         container.Register<IDistributedParameter, PKSimDistributedParameter>();

         //Register Factories
         container.RegisterFactory<ISimulationEngineFactory>();
         container.RegisterFactory<IChartDataToTableMapperFactory>();

         container.Register<IPKSimDimensionFactory, IDimensionFactory, PKSimDimensionFactory>(LifeStyle.Singleton);

         //Register opened types generics
         container.Register(typeof(IRepository<>), typeof(ImplementationRepository<>));

         container.Register<IInterpolation, LinearInterpolation>();
         container.Register<IPivoter, Pivoter>();

         container.Register<ISimulationSubject, Individual>();
         container.Register<Protocol, SimpleProtocol>();
         container.Register<Simulation, IndividualSimulation>();
         container.Register<Population, RandomPopulation>();

         //generic command registration
         container.Register<IOSPSuiteExecutionContext, ExecutionContext>();

         registerMoleculeFactories(container);
      }

      private void registerMoleculeFactories(IContainer container)
      {
         container.Register<IIndividualMoleculeFactory, IndividualEnzymeFactory>();
         container.Register<IIndividualMoleculeFactory, IndividualTransporterFactory>();
         container.Register<IIndividualMoleculeFactory, IndividualOtherProteinFactory>();
      }
   }
}