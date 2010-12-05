using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Bounce.Framework {
    public class BounceRunner {
        private static Bounce _bounce = new Bounce(System.Console.Out, System.Console.Error);
        private readonly ITargetsRetriever TargetsRetriever;
        private ILogOptionCommandLineTranslator LogOptionCommandLineTranslator;
        private readonly IParameterFinder ParameterFinder;
        private CommandAndTargetParser CommandAndTargetParser;

        public static IBounce Bounce {
            get { return _bounce; }
        }

        public BounceRunner() : this(new TargetsRetriever(), new LogOptionCommandLineTranslator(), new ParameterFinder()) {}

        public BounceRunner (ITargetsRetriever targetsRetriever, ILogOptionCommandLineTranslator logOptionCommandLineTranslator, IParameterFinder parameterFinder) {
            TargetsRetriever = targetsRetriever;
            LogOptionCommandLineTranslator = logOptionCommandLineTranslator;
            ParameterFinder = parameterFinder;
            CommandAndTargetParser = new CommandAndTargetParser();
        }

        public void Run(string[] args, MethodInfo getTargetsMethod) {
            var parameters = new CommandLineParameters();

            try {
                IDictionary<string, ITask> targets = GetTargetsFromAssembly(getTargetsMethod, parameters);

                ParsedCommandLineParameters parsedParameters = ParseCommandLineArguments(args);

                string[] buildArguments = parsedParameters.RemainingArguments;

                CommandAndTargets commandAndTargets = CommandAndTargetParser.ParseCommandAndTargetNames(buildArguments, targets);

                if (commandAndTargets.Targets.Count() >= 1) {
                    InterpretParameters(parameters, parsedParameters, _bounce);
                    EnsureAllRequiredParametersAreSet(parameters, commandAndTargets.Targets);

                    BuildTargets(commandAndTargets);
                }
                else
                {
                    System.Console.WriteLine("usage: bounce build|clean target-name");
                    PrintAvailableTargets(targets);
                    Environment.Exit(1);
                }
            } catch (BounceException ce) {
                ce.Explain(System.Console.Error);
                Environment.Exit(1);
            }
        }

        private void EnsureAllRequiredParametersAreSet(CommandLineParameters parameters, IEnumerable<Target> targetsToBuild) {
            var tasks = targetsToBuild.Select(t => t.Task);
            foreach (ITask task in tasks) {
                IEnumerable<IParameter> parametersForTask = ParameterFinder.FindParametersInTask(task);
                parameters.EnsureAllRequiredParametersHaveValues(parametersForTask);
            }
        }

        private void BuildTargets(CommandAndTargets commandAndTargets) {
            var builder = new TargetBuilder(_bounce);
            CommandAction commandAction = GetCommand(builder, commandAndTargets.Command);

            foreach(var target in commandAndTargets.Targets) {
                BuildTarget(target, commandAction);
            }
        }

        private void BuildTarget(Target target, CommandAction commandAction) {
            using (ITaskScope targetScope = _bounce.TaskScope(target.Task, commandAction.Command, target.Name)) {
                commandAction.Action(target.Task);
                targetScope.TaskSucceeded();
            }
        }

        private IDictionary<string, ITask> GetTargetsFromAssembly(MethodInfo getTargetsMethod, CommandLineParameters parameters) {
            return TargetsRetriever.GetTargetsFromAssembly(getTargetsMethod, parameters);
        }

        private void InterpretParameters(ICommandLineParameters parameters, ParsedCommandLineParameters parsedParameters, Bounce bounce) {
            LogOptionCommandLineTranslator.ParseCommandLine(parsedParameters, bounce);
            parameters.ParseCommandLineArguments(parsedParameters.Parameters);
        }

        public ParsedCommandLineParameters ParseCommandLineArguments(string [] args) {
            var parser = new CommandLineParameterParser();
            return parser.ParseCommandLineParameters(args);
        }

        private void PrintAvailableTargets(IDictionary<string, ITask> targets) {
            System.Console.WriteLine();
            System.Console.WriteLine("targets:");
            foreach (var target in targets) {
                System.Console.WriteLine("  " + target.Key);
                PrintAvailableParameters(ParameterFinder.FindParametersInTask(target.Value));
            }
        }

        private void PrintAvailableParameters(IEnumerable<IParameter> parameters) {
            if (parameters.Count() > 0) {
                foreach (var param in parameters) {
                    System.Console.Write("    /" + param.Name);
                    if (param.Required) {
                        System.Console.Write(" required");
                    }
                    if (param.HasValue) {
                        System.Console.Write(" default: " + param.DefaultValue);
                    }
                    System.Console.WriteLine();
                }
            }
        }

        private class CommandAction {
            public BounceCommand Command;
            public Action<ITask> Action;
        }

        private static CommandAction GetCommand(TargetBuilder builder, BounceCommand command) {
            switch (command) {
                case BounceCommand.Build:
                    return new CommandAction {Action = builder.Build, Command = command };
                case BounceCommand.Clean:
                    return new CommandAction { Action = builder.Clean, Command = command };
                default:
                    throw new ConfigurationException(String.Format("no such command {0}, try build or clean", command));
            }
        }
    }

    internal class CommandAndTargets {
        public BounceCommand Command;
        public IEnumerable<Target> Targets;
    }
}