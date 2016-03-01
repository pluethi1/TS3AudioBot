namespace TS3AudioBot.Algorithm
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Reflection;

	public class XCommandSystem
	{
		public static readonly CommandResultType[] AllTypes = Enum.GetValues(typeof(CommandResultType)).OfType<CommandResultType>().ToArray();

		ICommand rootCommand;

		public XCommandSystem(ICommand rootCommandArg)
		{
			rootCommand = rootCommandArg;
		}

		public ICommand RootCommand => rootCommand;

		public static IEnumerable<string> FilterList(IEnumerable<string> list, string filter)
		{
			// Convert result to list because it can be enumerated multiple times
			var possibilities = list.Select(t => new Tuple<string, int>(t, 0)).ToList();
			// Filter matching commands
			foreach (var c in filter)
			{
				var newPossibilities = (from p in possibilities
										let pos = p.Item1.IndexOf(c, p.Item2)
										where pos != -1
										select new Tuple<string, int>(p.Item1, pos + 1)).ToList();
				if (newPossibilities.Any())
					possibilities = newPossibilities;
			}
			// Take command with lowest index
			int minIndex = possibilities.Min(t => t.Item2);
			var cmds = possibilities.Where(t => t.Item2 == minIndex).Select(t => t.Item1).ToList();
			// Take the smallest command
			int minLength = cmds.Min(c => c.Length);

			return cmds.Where(c => c.Length == minLength);
		}

		internal ICommand AstToCommandResult(ASTNode node)
		{
			switch (node.Type)
			{
			case NodeType.Error:
				throw new CommandException("Found an unconvertable ASTNode of type Error");
			case NodeType.Command:
				var cmd = (ASTCommand)node;
				var arguments = new List<ICommand>();
				arguments.AddRange(cmd.Parameter.Select(n => AstToCommandResult(n)));
				return new AppliedCommand(rootCommand, new StaticEnumerableCommand(arguments));
			case NodeType.Value:
				return new StringCommand(((ASTValue)node).Value);
			}
			throw new NotSupportedException("Seems like there's a new NodeType, this code should not be reached");
		}

		public ICommandResult Execute(ExecutionInformation info, string command)
		{
			return Execute(info, command, new[] { CommandResultType.String, CommandResultType.Empty });
		}

		public ICommandResult Execute(ExecutionInformation info, string command, IEnumerable<CommandResultType> returnTypes)
		{
			var ast = CommandParser.ParseCommandRequest(command);
			var cmd = AstToCommandResult(ast);
			return cmd.Execute(info, new EmptyEnumerableCommand(), returnTypes);
		}

		public ICommandResult Execute(ExecutionInformation info, IEnumerableCommand arguments)
		{
			return Execute(info, arguments, new[] { CommandResultType.String, CommandResultType.Empty });
		}

		public ICommandResult Execute(ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			return rootCommand.Execute(info, arguments, returnTypes);
		}

		public string ExecuteCommand(ExecutionInformation info, string command)
		{
			ICommandResult result = Execute(info, command);
			if (result.ResultType == CommandResultType.String ||
				result.ResultType == CommandResultType.Empty)
				return result.ToString();
			throw new CommandException("Expected a string as result");
		}
	}

	#region Commands

	public class CommandGroup : ICommand
	{
		readonly IDictionary<string, ICommand> commands = new Dictionary<string, ICommand>();

		public void AddCommand(string name, ICommand command) => commands.Add(name, command);
		public void RemoveCommand(string name) => commands.Remove(name);
		public void RemoveCommand(ICommand command)
		{
			var commandPair = commands.Single(kvp => kvp.Value == command);
			commands.Remove(commandPair);
		}
		public bool ContainsCommand(string name) => commands.ContainsKey(name);

		public virtual ICommandResult Execute(ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			if (arguments.Count < 1)
			{
				if (returnTypes.Contains(CommandResultType.Command))
					return new CommandCommandResult(this);
				throw new CommandException("Expected a string");
			}

			var result = arguments.Execute(0, info, new EmptyEnumerableCommand(), new CommandResultType[] { CommandResultType.String });

			var commandResults = XCommandSystem.FilterList(commands.Keys, ((StringCommandResult)result).Content);
			if (commandResults.Skip(1).Any())
				throw new CommandException("Ambiguous command, possible names: " + string.Join(", ", commandResults));

			return commands[commandResults.First()].Execute(info, new EnumerableCommandRange(arguments, 1), returnTypes);
		}
	}

	public class FunctionCommand : ICommand
	{
		static readonly Type[] SpecialTypes
			= new Type[] { typeof(ExecutionInformation), typeof(IEnumerableCommand), typeof(IEnumerable<CommandResultType>) };

		// Needed for non-static member methods
		readonly object callee;
		readonly MethodInfo internCommand;
		readonly int normalParameters;
		/// <summary>
		/// How many free arguments have to be applied to this function.
		/// This includes only user-supplied arguments, e.g. the ExecutionInformation is not included.
		/// </summary>
		public int RequiredParameters { get; set; }

		public FunctionCommand(MethodInfo command, object obj = null)
		{
			internCommand = command;
			callee = obj;
			// Require all parameters by default
			normalParameters = internCommand.GetParameters().Count(p => !SpecialTypes.Contains(p.ParameterType));
			RequiredParameters = normalParameters;
		}

		// Provide some constructors that take lambda expressions directly
		public FunctionCommand(Action command) : this(command.Method, command.Target) { }
		public FunctionCommand(Func<string> command) : this(command.Method, command.Target) { }
		public FunctionCommand(Action<string> command) : this(command.Method, command.Target) { }
		public FunctionCommand(Func<string, string> command) : this(command.Method, command.Target) { }
		public FunctionCommand(Action<ExecutionInformation> command) : this(command.Method, command.Target) { }
		public FunctionCommand(Func<ExecutionInformation, string> command) : this(command.Method, command.Target) { }
		public FunctionCommand(Action<ExecutionInformation, string> command) : this(command.Method, command.Target) { }
		public FunctionCommand(Func<ExecutionInformation, string, string> command) : this(command.Method, command.Target) { }

		object ExecuteFunction(object[] parameters)
		{
			try
			{
				return internCommand.Invoke(callee, parameters);
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException;
			}
		}

		public virtual ICommandResult Execute(ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			object[] parameters = new object[internCommand.GetParameters().Length];
			// a: Iterate through arguments
			// p: Iterate through parameters
			int a = 0;
			for (int p = 0; p < parameters.Length; p++)
			{
				var arg = internCommand.GetParameters()[p].ParameterType;
				if (arg == typeof(ExecutionInformation))
					parameters[p] = info;
				else if (arg == typeof(IEnumerableCommand))
					parameters[p] = arguments;
				else if (arg == typeof(IEnumerable<CommandResultType>))
					parameters[p] = returnTypes;
				// Only add arguments if we still have some
				else if (a < arguments.Count)
				{
					var argResult = ((StringCommandResult)arguments.Execute(a, info, new EmptyEnumerableCommand(), new[] { CommandResultType.String })).Content;
					if (arg == typeof(string))
						parameters[p] = argResult;
					else if (arg == typeof(int) || arg == typeof(int?))
					{
						int intArg;
						if (!int.TryParse(argResult, out intArg))
							throw new CommandException("Can't convert parameter to int");
						parameters[p] = intArg;
					}
					else if (arg == typeof(string[]))
					{
						// Use the remaining arguments for this parameter
						var args = new string[arguments.Count - a];
						for (int i = 0; i < args.Length; i++, a++)
							args[i] = ((StringCommandResult)arguments.Execute(a, info, new EmptyEnumerableCommand(),
								new[] { CommandResultType.String })).Content;
						parameters[p] = args;
						// Correct the argument index to the last used argument
						a--;
					}
					else
						throw new CommandException("Found inconvertable parameter type: " + arg.Name);
					a++;
				}
			}
			// Check if we were able to set enough arguments
			if (a < Math.Min(parameters.Length, RequiredParameters))
			{
				if (returnTypes.Contains(CommandResultType.Command))
				{
					if (arguments.Count == 0)
						return new CommandCommandResult(this);
					return new CommandCommandResult(new AppliedCommand(this, arguments));
				}
				throw new CommandException("Not enough arguments for function " + internCommand.Name);
			}

			if (internCommand.ReturnType == typeof(ICommandResult))
				return (ICommandResult)ExecuteFunction(parameters);

			bool executed = false;
			object result = null;
			// Take first fitting command result
			foreach (var returnType in returnTypes)
			{
				switch (returnType)
				{
				case CommandResultType.Command:
					// Return a command if possible
					// Only do this if the command was not yet executed to prevent executing a command more than once
					if (!executed &&
						(internCommand.GetParameters().Any(p => p.ParameterType == typeof(string[])) ||
						 a < normalParameters))
						return new CommandCommandResult(new AppliedCommand(this, arguments));
					break;
				case CommandResultType.Empty:
					if (!executed)
						ExecuteFunction(parameters);
					return new EmptyCommandResult();
				case CommandResultType.Enumerable:
					if (internCommand.ReturnType == typeof(string[]))
					{
						if (!executed)
							result = ExecuteFunction(parameters);
						return new StaticEnumerableCommandResult(((string[])result).Select(s => new StringCommandResult(s)));
					}
					break;
				case CommandResultType.String:
					if (!executed)
					{
						result = ExecuteFunction(parameters);
						executed = true;
					}
					if (result != null && !string.IsNullOrEmpty(result.ToString()))
						return new StringCommandResult(result.ToString());
					break;
				}
			}
			// Try to return an empty string
			if (returnTypes.Contains(CommandResultType.String) && executed)
				return new StringCommandResult("");
			throw new CommandException("Couldn't find a proper command result for function " + internCommand.Name);
		}

		/// <summary>
		/// A conveniance method to set the amount of required parameters and returns this object.
		/// This is useful for method chaining.
		/// </summary>
		public FunctionCommand SetRequiredParameters(int required)
		{
			RequiredParameters = required;
			return this;
		}
	}

	/// <summary>
	/// A special group command that also accepts commands as first parameter and executes them on the left over parameters.
	/// </summary>
	public class RootCommand : CommandGroup
	{
		public override ICommandResult Execute(ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			if (arguments.Count < 1)
				return base.Execute(info, arguments, returnTypes);

			var result = arguments.Execute(0, info, new EmptyEnumerableCommand(), new CommandResultType[] { CommandResultType.Command, CommandResultType.String });
			if (result.ResultType == CommandResultType.String)
				return base.Execute(info, arguments, returnTypes);

			return ((CommandCommandResult)result).Command.Execute(info, new EnumerableCommandRange(arguments, 1), returnTypes);
		}
	}

	public interface ICommand
	{
		/// <summary>
		/// Execute this command.
		/// </summary>
		/// <param name="info">All global informations for this execution.</param>
		/// <param name="arguments">
		/// The arguments for this command.
		/// They are evaluated lazy which means they will only be evaluated if needed
		/// </param>
		/// <param name="returnTypes">
		/// The possible return types that should be returned by this execution.
		/// They are ordered by priority so, if possible, the first return type should be picked, then the second and so on.
		/// </param>
		ICommandResult Execute(ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes);
	}

	public class StringCommand : ICommand
	{
		readonly string content;

		public StringCommand(string contentArg)
		{
			content = contentArg;
		}

		public ICommandResult Execute(ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			return new StringCommandResult(content);
		}
	}

	public class AppliedCommand : ICommand
	{
		readonly ICommand internCommand;
		readonly IEnumerableCommand internArguments;

		public AppliedCommand(ICommand command, IEnumerableCommand arguments)
		{
			internCommand = command;
			internArguments = arguments;
		}

		public ICommandResult Execute(ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			return internCommand.Execute(info, new EnumerableCommandMerge(new IEnumerableCommand[] { internArguments, arguments }), returnTypes);
		}
	}

	public class ExecutionInformation
	{
		public BotSession Session { get; set; }
		public TS3Query.Messages.TextMessage TextMessage { get; set; }
		public Lazy<bool> IsAdmin { get; set; }
	}

	#endregion

	#region EnumerableCommands

	public interface IEnumerableCommand
	{
		int Count { get; }

		ICommandResult Execute(int index, ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes);
	}

	public class EmptyEnumerableCommand : IEnumerableCommand
	{
		public int Count => 0;

		public ICommandResult Execute(int index, ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			throw new CommandException("No arguments given");
		}
	}

	public class StaticEnumerableCommand : IEnumerableCommand
	{
		readonly IEnumerable<ICommand> internArguments;

		public int Count => internArguments.Count();

		public StaticEnumerableCommand(IEnumerable<ICommand> arguments)
		{
			internArguments = arguments;
		}

		public ICommandResult Execute(int index, ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			if (index < 0 || index >= internArguments.Count())
				throw new CommandException("Requested too many arguments (StaticEnumerableCommand)");
			return internArguments.ElementAt(index).Execute(info, arguments, returnTypes);
		}
	}

	public class EnumerableCommandRange : IEnumerableCommand
	{
		readonly IEnumerableCommand internCommand;
		readonly int start;
		readonly int count;

		public int Count => Math.Min(internCommand.Count - start, count);

		public EnumerableCommandRange(IEnumerableCommand command, int startArg, int countArg = int.MaxValue)
		{
			internCommand = command;
			start = startArg;
			count = countArg;
		}

		public ICommandResult Execute(int index, ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			if (index < 0)
				throw new CommandException("Negative arguments?? (EnumerableCommandRange)");
			return internCommand.Execute(index + start, info, arguments, returnTypes);
		}
	}

	public class EnumerableCommandMerge : IEnumerableCommand
	{
		readonly IEnumerable<IEnumerableCommand> internCommands;

		public int Count => internCommands.Select(c => c.Count).Sum();

		public EnumerableCommandMerge(IEnumerable<IEnumerableCommand> commands)
		{
			internCommands = commands;
		}

		public ICommandResult Execute(int index, ExecutionInformation info, IEnumerableCommand arguments, IEnumerable<CommandResultType> returnTypes)
		{
			if (index < 0)
				throw new CommandException("Negative arguments?? (EnumerableCommandMerge)");
			foreach (var c in internCommands)
			{
				if (index < c.Count)
					return c.Execute(index, info, arguments, returnTypes);
				index -= c.Count;
			}
			throw new CommandException("Requested too many arguments (EnumerableCommandMerge)");
		}
	}

	#endregion

	#region CommandResults

	public enum CommandResultType
	{
		Empty,
		Command,
		Enumerable,
		String
	}

	public abstract class ICommandResult
	{
		public abstract CommandResultType ResultType { get; }

		public override string ToString()
		{
			if (ResultType == CommandResultType.String)
				return ((StringCommandResult)this).Content;
			if (ResultType == CommandResultType.Empty)
				return string.Empty;
			return "CommandResult can't be converted into a string";
		}
	}

	public class EmptyCommandResult : ICommandResult
	{
		public override CommandResultType ResultType => CommandResultType.Empty;
	}

	public class CommandCommandResult : ICommandResult
	{
		readonly ICommand command;

		public override CommandResultType ResultType => CommandResultType.Command;

		public virtual ICommand Command => command;

		public CommandCommandResult(ICommand commandArg)
		{
			command = commandArg;
		}
	}

	public abstract class EnumerableCommandResult : ICommandResult
	{
		public override CommandResultType ResultType => CommandResultType.Enumerable;

		public abstract int Count { get; }

		public abstract ICommandResult this[int index] { get; }
	}

	public class EnumerableCommandResultRange : EnumerableCommandResult
	{
		readonly EnumerableCommandResult internResult;
		readonly int start;
		readonly int count;

		public override int Count => Math.Min(internResult.Count - start, count);

		public override ICommandResult this[int index]
		{
			get
			{
				if (index >= count)
					throw new IndexOutOfRangeException($"{index} >= {count}");
				return internResult[index + start];
			}
		}

		public EnumerableCommandResultRange(EnumerableCommandResult internResultArg, int startArg, int countArg = int.MaxValue)
		{
			internResult = internResultArg;
			start = startArg;
			count = countArg;
		}
	}

	public class EnumerableCommandResultMerge : EnumerableCommandResult
	{
		readonly IEnumerable<EnumerableCommandResult> internResult;

		public override int Count => internResult.Select(r => r.Count).Sum();

		public override ICommandResult this[int index]
		{
			get
			{
				foreach (var r in internResult)
				{
					if (r.Count < index)
						return r[index];
					index -= r.Count;
				}
				throw new IndexOutOfRangeException("Not enough content available");
			}
		}

		public EnumerableCommandResultMerge(IEnumerable<EnumerableCommandResult> internResultArg)
		{
			internResult = internResultArg;
		}
	}

	public class StaticEnumerableCommandResult : EnumerableCommandResult
	{
		readonly IEnumerable<ICommandResult> content;

		public override int Count => content.Count();

		public override ICommandResult this[int index] => content.ElementAt(index);

		public StaticEnumerableCommandResult(IEnumerable<ICommandResult> contentArg)
		{
			content = contentArg;
		}
	}

	public class StringCommandResult : ICommandResult
	{
		readonly string content;

		public override CommandResultType ResultType => CommandResultType.String;
		public virtual string Content => content;

		public StringCommandResult(string contentArg)
		{
			content = contentArg;
		}
	}

	#endregion

	public class CommandException : Exception
	{
		public CommandException(string message) : base(message) { }
		public CommandException(string message, Exception inner) : base(message, inner) { }
	}
}