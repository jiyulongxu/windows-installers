﻿using System;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using Elastic.Configuration.EnvironmentBased;
using Elastic.Configuration.FileBased.Yaml;
using Elastic.Installer.Domain.Configuration.Wix;
using Elastic.Installer.Domain.Model.Base;
using ReactiveUI;

namespace Elastic.Installer.Domain.Model.Elasticsearch.Locations
{
	public class LocationsModel : StepBase<LocationsModel, LocationsModelValidator>
	{
		private const string ProgramDataEnvironmentVariable = "ALLUSERSPROFILE";
		private const string Logs = "logs";
		private const string Data = "data";
		private const string Config = "config";
		private const string DefaultWritableDirectoryArgument = @"[%" + ProgramDataEnvironmentVariable + @"]\" + CompanyFolderName + @"\" + ProductFolderName;
		private const string DefaultLogsDirectoryArgument = DefaultWritableDirectoryArgument + @"\" + Logs;
		private const string DefaultDataDirectoryArgument = DefaultWritableDirectoryArgument + @"\" + Data;
		private const string DefaultConfigDirectoryArgument = DefaultWritableDirectoryArgument + @"\" + Config;

		public const string CompanyFolderName = "Elastic";
		public const string ProductFolderName = "Elasticsearch";
		public const string ConfigDirectoryExists = nameof(ConfigDirectoryExists);
		public const string LogsDirectoryExists = nameof(LogsDirectoryExists);
		public const string DataDirectoryExists = nameof(DataDirectoryExists);

		public static string DefaultProgramFiles => 
			Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

		public static readonly string DefaultCompanyDataDirectory =
			Path.Combine(Environment.GetEnvironmentVariable(ProgramDataEnvironmentVariable), CompanyFolderName);

		public static readonly string DefaultProductDataDirectory = Path.Combine(DefaultCompanyDataDirectory, ProductFolderName);
		public static readonly string DefaultCompanyInstallationDirectory = Path.Combine(DefaultProgramFiles, CompanyFolderName);
		public static readonly string DefaultProductInstallationDirectory = Path.Combine(DefaultCompanyInstallationDirectory, ProductFolderName);
		public static readonly string DefaultMsiLogFileLocation = Path.Combine(DefaultProductDataDirectory, "install.log");
		public static readonly string DefaultLogsDirectory = Path.Combine(DefaultProductDataDirectory, Logs);
		public static readonly string DefaultDataDirectory = Path.Combine(DefaultProductDataDirectory, Data);
		public static readonly string DefaultConfigDirectory = Path.Combine(DefaultProductDataDirectory, Config);
		
		public string DefaultProductVersionInstallationDirectory => Path.Combine(DefaultProductInstallationDirectory, CurrentVersion);

		private bool _refreshing;
		private readonly ElasticsearchEnvironmentConfiguration _elasticsearchEnvironmentConfiguration;
		private readonly ElasticsearchYamlConfiguration _yamlConfiguration;

		private string CurrentVersion { get; }
		private string ExistingVersion { get; }

		public LocationsModel(
			ElasticsearchEnvironmentConfiguration elasticsearchEnvironmentConfiguration, 
			ElasticsearchYamlConfiguration yamlConfiguration, 
			VersionConfiguration versionConfig, 
			IFileSystem fileSystem)
		{
			this.IsRelevant = !versionConfig.ExistingVersionInstalled;
			this.Header = "Locations";
			this._elasticsearchEnvironmentConfiguration = elasticsearchEnvironmentConfiguration;
			this._yamlConfiguration = yamlConfiguration;
			this.CurrentVersion = versionConfig.CurrentVersion.ToString();
			this.ExistingVersion = versionConfig.UpgradeFromVersion?.ToString();
			this.FileSystem = fileSystem;

			this.Refresh();
			this._refreshing = true;

			this.WhenAny(
				vm => vm.LogsDirectory,
				(c) => {
					var v = c.GetValue();
					return this.FileSystem.Path.IsPathRooted(v) 
						? this.FileSystem.Path.Combine(v, "elasticsearch.log") 
						: null;
				})
				.ToProperty(this, vm => vm.ElasticsearchLog, out elasticsearchLog);

			this.ThrownExceptions.Subscribe(e =>
			{
			});

			//If install, config, logs or data dir are set force ConfigureLocations to true
			this.WhenAny(
				vm => vm.InstallDir,
				vm => vm.ConfigDirectory,
				vm => vm.LogsDirectory,
				vm => vm.DataDirectory,
				(i, c, l, d) => !this._refreshing
				)
				.Subscribe(x => { if (x) this.ConfigureLocations = true; });

			this.WhenAny(
				vm => vm.ConfigureLocations,
				(c) => !this._refreshing && !c.Value
				)
				.Subscribe(x => { if (x) this.Refresh(); });

			this._refreshing = false;
		}

		public sealed override void Refresh()
		{
			this._refreshing = true;
			this.ConfigureLocations = false;
			this.SetDefaultLocations();
			this._refreshing = false;
		}

		public void SetDefaultLocations()
		{
			this._refreshing = true;

			//todo duplication?
			this.InstallDir = this._elasticsearchEnvironmentConfiguration.TargetInstallationDirectory ?? DefaultProductInstallationDirectory;
			this.ConfigDirectory = this._elasticsearchEnvironmentConfiguration.TargetInstallationConfigDirectory ?? DefaultConfigDirectory;
			this.PreviousInstallationDirectory = this._elasticsearchEnvironmentConfiguration.PreviousInstallationDirectory;
			this.DataDirectory = this._yamlConfiguration?.Settings?.DataPath ?? DefaultDataDirectory;
			this.LogsDirectory = this._yamlConfiguration?.Settings?.LogsPath ?? DefaultLogsDirectory;

			var home = this._elasticsearchEnvironmentConfiguration.TargetInstallationDirectory ?? DefaultProductInstallationDirectory;
			var config = this._elasticsearchEnvironmentConfiguration.TargetInstallationConfigDirectory ?? DefaultConfigDirectory;
			var data = this._yamlConfiguration?.Settings?.DataPath ?? DefaultDataDirectory;
			var logs = this._yamlConfiguration?.Settings?.LogsPath ?? DefaultLogsDirectory;

			this.ConfigureLocations = 
				!this.SamePathAs(home, DefaultProductInstallationDirectory)
				|| !this.SamePathAs(config, DefaultConfigDirectory)
				|| !this.SamePathAs(data, DefaultDataDirectory)
				|| !this.SamePathAs(logs, DefaultLogsDirectory);
			this._refreshing = false;
		}

		private bool SamePathAs(string pathA, string pathB)
		{
			if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)) return false;
			
			var fullPathA = this.FileSystem.Path.GetFullPath(pathA).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var fullPathB = this.FileSystem.Path.GetFullPath(pathB).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return 0 == string.Compare(fullPathA, fullPathB, StringComparison.OrdinalIgnoreCase);

		}

		readonly ObservableAsPropertyHelper<string> elasticsearchLog;
		public string ElasticsearchLog => elasticsearchLog.Value;

		bool configureLocations;
		public bool ConfigureLocations
		{
			get => configureLocations;
			set => this.RaiseAndSetIfChanged(ref configureLocations, value);
		}

		string installDirectory;
		[Argument(nameof(InstallDir), PersistInRegistry = true)]
		public string InstallDir
		{
			get => string.IsNullOrWhiteSpace(installDirectory) ? installDirectory : ForceVersionFolder(installDirectory);
			set
			{
				var versionFolder = ForceVersionFolder(value);
				this.RaiseAndSetIfChanged(ref installDirectory, versionFolder);
			}
		}

		string previousInstallationDirectory;
		[SetPropertyActionArgument(nameof(PreviousInstallationDirectory), "[%" + ElasticsearchEnvironmentStateProvider.EsHome + "]", false)]
		public string PreviousInstallationDirectory
		{
			get => previousInstallationDirectory;
			set => this.RaiseAndSetIfChanged(ref previousInstallationDirectory, value);
		}

		private string ForceVersionFolder(string directory)
		{
			if (string.IsNullOrWhiteSpace(directory)) return directory;
			if (!Path.IsPathRooted(directory)) return directory;
			return Path.Combine(GetRootedPathIfNecessary(directory), CurrentVersion);
		}

		private string GetRootedPathIfNecessary(string value)
		{
			if (string.IsNullOrEmpty(value)) return value;
			try
			{
				var directory = this.FileSystem.DirectoryInfo.FromDirectoryName(value);
				if (directory.Name.Equals(CurrentVersion, StringComparison.OrdinalIgnoreCase)
				    || directory.Name.Equals(ExistingVersion, StringComparison.OrdinalIgnoreCase))
					return directory.Parent?.FullName;
			}
			catch
			{
				// ignored
			}
			return value;
		}

		string dataDirectory;
		[SetPropertyActionArgument(nameof(DataDirectory), DefaultDataDirectoryArgument, PersistInRegistry = true)]
		public string DataDirectory
		{
			get => dataDirectory;
			set => this.RaiseAndSetIfChanged(ref dataDirectory, value);
		}

		string configDirectory;
		[SetPropertyActionArgument(nameof(ConfigDirectory), DefaultConfigDirectoryArgument, PersistInRegistry = true)]
		public string ConfigDirectory
		{
			get => configDirectory;
			set => this.RaiseAndSetIfChanged(ref configDirectory, value);
		}

		string logsDirectory;
		[SetPropertyActionArgument(nameof(LogsDirectory), DefaultLogsDirectoryArgument, PersistInRegistry = true)]
		public string LogsDirectory
		{
			get => logsDirectory;
			set => this.RaiseAndSetIfChanged(ref logsDirectory, value);
		}

		public IFileSystem FileSystem { get; }

		public override string ToString()
		{
			var sb = new StringBuilder();
			sb.AppendLine(nameof(LocationsModel));
			sb.AppendLine($"- {nameof(IsValid)} = " + IsValid);
			sb.AppendLine($"- {nameof(ConfigureLocations)} = " + ConfigureLocations);
			sb.AppendLine($"- {nameof(InstallDir)} = " + InstallDir);
			sb.AppendLine($"- {nameof(PreviousInstallationDirectory)} = " + PreviousInstallationDirectory);
			sb.AppendLine($"- {nameof(DataDirectory)} = " + DataDirectory);
			sb.AppendLine($"- {nameof(ConfigDirectory)} = " + ConfigDirectory);
			sb.AppendLine($"- {nameof(LogsDirectory)} = " + LogsDirectory);
			return sb.ToString();
		}
	}
}
