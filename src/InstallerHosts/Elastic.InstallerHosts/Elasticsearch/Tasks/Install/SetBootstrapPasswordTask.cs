﻿using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Elastic.Configuration.EnvironmentBased;
using Elastic.Installer.Domain.Configuration.Wix.Session;
using Elastic.Installer.Domain.Model.Elasticsearch;
using Elastic.Installer.Domain.Model.Elasticsearch.XPack;

namespace Elastic.InstallerHosts.Elasticsearch.Tasks.Install
{
	public class SetBootstrapPasswordTask : ElasticsearchInstallationTaskBase
	{
		public SetBootstrapPasswordTask(string[] args, ISession session) 
			: base(args, session) {}

		public SetBootstrapPasswordTask(ElasticsearchInstallationModel model, ISession session, IFileSystem fileSystem) 
			: base(model, session, fileSystem) {}

		protected override bool ExecuteTask()
		{
			var xPackModel = this.InstallationModel.XPackModel;
			if (!xPackModel.IsRelevant || !xPackModel.XPackSecurityEnabled || 
				xPackModel.XPackLicense != XPackLicenseMode.Trial || string.IsNullOrEmpty(xPackModel.XPackLicenseFile) || 
				(!string.IsNullOrEmpty(xPackModel.XPackLicenseFile) && xPackModel.UploadedXPackLicense == "basic")) return true;

			var installationDir = this.InstallationModel.LocationsModel.InstallDir;
			var password = this.InstallationModel.XPackModel.BootstrapPassword;
			var binary = this.FileSystem.Path.Combine(installationDir, "bin", "elasticsearch-keystore.bat");

			var p = new Process
			{
				EnableRaisingEvents = true,
				StartInfo =
				{
					FileName = binary,
					Arguments = "add bootstrap.password -xf",
					ErrorDialog = false,
					CreateNoWindow = true,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					RedirectStandardInput = true
				}
			};

			p.StartInfo.EnvironmentVariables[ElasticsearchEnvironmentStateProvider.ConfDir] =
				this.InstallationModel.LocationsModel.ConfigDirectory;

			void OnDataReceived(object sender, DataReceivedEventArgs a)
			{
				var message = a.Data;
				if (message != null) this.Session.Log(message);
			}

			var errors = false;

			void OnErrorsReceived(object sender, DataReceivedEventArgs a)
			{
				var message = a.Data;
				if (message != null)
				{
					errors = true;
					this.Session.Log(message);
				}
			}

			p.ErrorDataReceived += OnErrorsReceived;
			p.OutputDataReceived += OnDataReceived;
			p.Start();

			p.StandardInput.WriteLine(password);
			p.StandardInput.Close();

			p.BeginOutputReadLine();
			p.BeginErrorReadLine();
			p.WaitForExit();

			var exitCode = p.ExitCode;
			if (exitCode != 0)
				this.Session.Log($"elasticsearch-keystore process returned non-zero exit code: {exitCode}");

			p.ErrorDataReceived -= OnErrorsReceived;
			p.OutputDataReceived -= OnDataReceived;
			p.Close();

			return !errors && exitCode == 0;
		}
	}
}