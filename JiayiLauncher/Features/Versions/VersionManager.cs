﻿using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Management.Deployment;
using JiayiLauncher.Features.Game;
using JiayiLauncher.Settings;
using JiayiLauncher.Utils;

namespace JiayiLauncher.Features.Versions;

public static class VersionManager
{
	public enum SwitchResult
	{
		Succeeded,
		VersionNotFound,
		DeveloperModeDisabled,
		UnknownError
	}
	
	public static event EventHandler<EventArgs>? DownloadFinished; 

	public static bool VersionInstalled(string ver)
	{
		Directory.CreateDirectory(JiayiSettings.Instance!.VersionsPath);
		var folders = Directory.GetDirectories(JiayiSettings.Instance!.VersionsPath);
		return folders.Any(x => x.Contains(ver));
	}

	public static async Task DownloadVersion(MinecraftVersion version, IProgress<int> progress)
	{
		var updateId = version.Archs.x64!.UpdateIds[0];
		var url = await RequestFactory.GetDownloadUrl(updateId);
		var fileName = version.Archs.x64.FileName;
		var filePath = Path.Combine(JiayiSettings.Instance!.VersionsPath, fileName);

		if (File.Exists(filePath)) File.Delete(filePath);

		using var client = new HttpClient();
		using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
		
		if (!response.IsSuccessStatusCode) return;
		
		var contentLength = response.Content.Headers.ContentLength;
		var totalRead = 0L;
		var buffer = new byte[1048576]; // 1 MB buffer
		
		await using var stream = await response.Content.ReadAsStreamAsync();
		await using var fileStream = new FileStream(filePath, FileMode.Create);
		
		progress.Report(0);

		while (true)
		{
			var read = await stream.ReadAsync(buffer);
			if (read == 0) break;
			
			await fileStream.WriteAsync(buffer.AsMemory(0, read));
			totalRead += read;
			progress.Report((int)(totalRead * 100 / contentLength)!);
		}
		
		progress.Report(100);
		
		fileStream.Close();
		stream.Close();
		
		var folder = Path.Combine(JiayiSettings.Instance.VersionsPath, version.Version);
		Directory.CreateDirectory(folder);
		ZipFile.ExtractToDirectory(filePath, folder);
		File.Delete(filePath);
		
		// DELETE AppxSignature.p7x so that the game can be installed with developer mode
		var signature = Path.Combine(folder, "AppxSignature.p7x");
		if (File.Exists(signature)) File.Delete(signature);
		
		DownloadFinished?.Invoke(null, EventArgs.Empty);
	}

	public static async Task RemoveVersion(string ver)
	{
		await Task.Run(() =>
		{
			var folders = Directory.GetDirectories(JiayiSettings.Instance!.VersionsPath);
			var folder = folders.FirstOrDefault(x => x.Contains(ver));
			if (folder == null) return;

			Directory.Delete(folder, true);
		});
	}
	
	// my favorite part of this class
	public static async Task<SwitchResult> Switch(string version)
	{
		Log.Write(nameof(VersionManager), $"Switching to version {version}");
		
		var folders = Directory.GetDirectories(JiayiSettings.Instance!.VersionsPath);
		var folder = folders.FirstOrDefault(x => x.Contains(version));
		if (folder == null) return SwitchResult.VersionNotFound;
		
		var packages = PackageData.PackageManager.FindPackages("Microsoft.MinecraftUWP_8wekyb3d8bbwe");
		foreach (var package in packages)
		{
			if (package.InstalledPath.Contains(version))
			{
				Log.Write(nameof(VersionManager), "Version already installed");
				return SwitchResult.Succeeded;
			}
			
			if (package.IsDevelopmentMode)
				await PackageData.PackageManager.RemovePackageAsync(package.Id.FullName, RemovalOptions.PreserveApplicationData);
			else
			{
				Directory.Move(PackageData.GetGameDataPath(), JiayiSettings.Instance.VersionsPath);
				await PackageData.PackageManager.RemovePackageAsync(package.Id.FullName, 0);
			}
		}
		
		Log.Write(nameof(VersionManager), "Registering package");
		
		var manifest = Path.Combine(folder, "AppxManifest.xml");
		var result = await PackageData.PackageManager.RegisterPackageAsync(new Uri(manifest), null,
				DeploymentOptions.DevelopmentMode);

		if (result.IsRegistered)
		{
			Log.Write(nameof(VersionManager), "Package registered");
			
			var path = Path.Combine(JiayiSettings.Instance.VersionsPath, "Microsoft.MinecraftUWP_8wekyb3d8bbwe");
			if (Directory.Exists(path))
				Directory.Move(path, PackageData.GetGameDataPath());
			
			return SwitchResult.Succeeded;
		}

		if (result.ErrorText.Contains("sideload"))
		{
			Log.Write(nameof(VersionManager), "Developer mode disabled");
			return SwitchResult.DeveloperModeDisabled;
		}

		Log.Write(nameof(VersionManager), $"Unknown error: {result.ErrorText}");
		return SwitchResult.UnknownError;
	}
}