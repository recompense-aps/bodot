using System.Linq;
using System.IO;
using System.IO.Compression;
using static Bodot.Output;

namespace Bodot
{
	public class InfoCommand : Command
	{
		public override string Name() => nameof(InfoCommand);
		public override bool ShouldExecute(BodotCommandLine commandLine) => commandLine.Info;
		public override (object? data, bool success) Execute(BodotCommandLine commandLine)
		{
			Out(
				$"|-----------|",
				$"|---Bodot---|",
				$"|--v0.0.0---|",
				$"|-----------|\n"
			);

			Out(
				LocalBodotConfig.LastLoadedString
			);

			if (!File.Exists("./bodot.config"))
				Out("[!] No config found in current directory\n", ConsoleColor.Yellow, ConsoleColor.Black);
			
			if (!File.Exists(LocalBodotConfig.Instance.GodotFilePath))
				Out("[!] Godot binary path is invalid\n", ConsoleColor.Yellow, ConsoleColor.Black);

			return (null, true);
		}
		
	}

	public class InitCommand : Command
	{
		public override string Name() => nameof(InitCommand);
		public override bool ShouldExecute(BodotCommandLine commandLine) => commandLine.Init;
		public override (object? data, bool success) Execute(BodotCommandLine commandLine)
		{
			var config = LocalBodotConfig.Instance;

			config.ProjectName = Ask("What is the name of your project?").Trim();
			config.MetaVersion = Ask("What is the meta version of your project?").Trim();
			config.GodotFilePath = AskUntil(
				"What is the path to your godot binary?", 
				"Could not find a valid godot binary at specified path, please try again",
				input => File.Exists(input.Trim())
			).Trim();
			config.ExportOutputPath = AskUntil(
				"What is the path to export your project to?", 
				"Could not find the specified directory, please try again",
				input => Directory.Exists(input.Trim())
			).Trim();

			LocalBodotConfig.Save();

			return ("Successfully configured project", true);
		}
		
	}

	public class ConfigCommand : Command
	{
		private static readonly Dictionary<string, Action<string>> settings = new Dictionary<string, Action<string>>()
		{
			{ "UseLog", value => LocalBodotConfig.Instance.UseLog = value.ToLower() == "true" },
			{ "GodotFilePath", value => LocalBodotConfig.Instance.GodotFilePath = value },
			{ "MetaVersion", value => LocalBodotConfig.Instance.MetaVersion = value },
			{ "MajorVersion", value => LocalBodotConfig.Instance.MajorVersion = value },
			{ "MinorVersion", value => LocalBodotConfig.Instance.MinorVersion = value },
			{ "PatchVersion", value => LocalBodotConfig.Instance.PatchVersion = value },
			{ "ProjectName", value => LocalBodotConfig.Instance.ProjectName = value },
			{ "AutoIncrementPath", value => LocalBodotConfig.Instance.AutoIncrementPatch = value.ToLower() == "true" }
		};

		public override string Name() => nameof(ConfigCommand);
		public override bool ShouldExecute(BodotCommandLine commandLine) => commandLine.Config;
		public override (object? data, bool success) Execute(BodotCommandLine commandLine)
		{
			var setting = commandLine.Args?.ElementAtOrDefault(0);
			var value = commandLine.Args?.ElementAtOrDefault(1);

			Assert(IsSet(setting), "missing config setting");
			Assert(IsSet(value), "missing config value");

			setting = Get(setting);
			value = Get(value);

			Assert(settings.ContainsKey(setting), $"'{setting}' is not configurable or it doesn't exist");
			
			settings[setting](value);

			LocalBodotConfig.Save();

			return ($"Configured {setting}={value}", true);
		}
		
	}

	public class BuildCommand : Command
	{
		public override string Name() => nameof(BuildCommand);
		public override bool ShouldExecute(BodotCommandLine commandLine) => commandLine.Build;
		public override (object? data, bool success) Execute(BodotCommandLine commandLine)
		{
			if (!File.Exists("./bodot.config"))
				return ("No config found in current directory", false);

			if (!File.Exists("./export_presets.cfg"))
				return ("No export_presets.cfg in current directory. Please configure exports in the godot editor", false);

			var exportConfigFile = File.ReadAllLines("./export_presets.cfg");
			var root = $"./{LocalBodotConfig.Instance.ExportOutputPath}/{LocalBodotConfig.Instance.SemanticVersion}";
			var presets = exportConfigFile
				.Where(x => x.Contains("name=") && !x.Contains("_"))
				.Select(x => x.Split('=').ElementAtOrDefault(1) ?? "")
				.Where(x => !string.IsNullOrEmpty(x))
			;
			var betterPresets = presets.Select(x => x.ToLower().Replace(" ", "-").Replace("\"", "").Replace("/", "-"));

			if (presets.Count() < 1)
				return ("No export presets could be found. Please configure exports in the godot editor", false);

			if (commandLine.Overwrite && Directory.Exists(root))
			{
				Warn($"Overwriting {LocalBodotConfig.Instance.SemanticVersion}");
				Directory.Delete(root, true);
			}

			if (Directory.Exists(root))
				return ($"Build directory {root} already exisits. Please use the --overwrite option to explicitly overwrite build", false);

			Info("Exporting for presets: " + string.Join(",", presets) + "\n");

			foreach(var (preset, betterPreset) in presets.Zip(betterPresets))
			{
				Out($"\n========================BEGIN {preset}========================\n", ConsoleColor.DarkCyan, ConsoleColor.Black);

				var presetFolder = $"{root}/{betterPreset}";
				var fileName = $"{presetFolder}/{LocalBodotConfig.Instance.ComputedExportName}" + (betterPreset.Contains("windows") ? ".exe" : "");
				
				Directory.CreateDirectory(presetFolder);

				Terminal.Create()
					.AsGodot()
					.WithArg("--export")
					.WithArg("--no-window")
					.WithArg($"{preset}")
					.WithArg(fileName)
					.Execute();

				foreach(var file in LocalBodotConfig.Instance.FilesToCopyPostBuild)
				{
					if (!File.Exists(file))
					{
						Warn($"Cannot copy '{file}'. It does not exist.");
						continue;
					}

					File.Copy(file, $"{presetFolder}/{Path.GetFileName(file)}");
					Success($"Coppied '{file}'");
				}

				foreach(var dir in LocalBodotConfig.Instance.DirectoriesToCopyPostBuild)
				{
					if (!Directory.Exists(dir))
					{
						Warn($"Cannot copy '{dir}'. It does not exist.");
						continue;
					}

					Copy(dir, presetFolder + "/" + dir.Replace("./", ""));
					Success($"Coppied '{dir}'");
				}

			
				if (commandLine.Zip)
				{
					Out("Creating zip archive...");
					var zipName = $"./{LocalBodotConfig.Instance.ExportOutputPath}/{LocalBodotConfig.Instance.SemanticVersion}-{betterPreset}.zip";
					ZipFile.CreateFromDirectory($"{root}/{betterPreset}", zipName);

					File.Move(zipName, $"./{root}/{betterPreset}/{LocalBodotConfig.Instance.SemanticVersion}-{betterPreset}.zip");
				}

				Out($"\n========================END {preset}========================\n", ConsoleColor.DarkCyan, ConsoleColor.Black);
			}

			if (LocalBodotConfig.Instance.AutoIncrementPatch && !commandLine.Overwrite)
			{
				LocalBodotConfig.ChangePatch(1);
				LocalBodotConfig.Save();
			}

			return (null, true);
		}

		public static void Copy(string sourceDirectory, string targetDirectory)
		{
			DirectoryInfo diSource = new DirectoryInfo(sourceDirectory);
			DirectoryInfo diTarget = new DirectoryInfo(targetDirectory);

			CopyAll(diSource, diTarget);
		}

		public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
		{
			Directory.CreateDirectory(target.FullName);

			// Copy each file into the new directory.
			foreach (FileInfo fi in source.GetFiles())
			{
				Info($"Copying {target.FullName}/{fi.Name}");
				fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
			}

			// Copy each subdirectory using recursion.
			foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
			{
				DirectoryInfo nextTargetSubDir =
					target.CreateSubdirectory(diSourceSubDir.Name);
				CopyAll(diSourceSubDir, nextTargetSubDir);
			}
		}
	}
}