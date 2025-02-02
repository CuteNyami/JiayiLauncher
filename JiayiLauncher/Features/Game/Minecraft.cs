﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using JiayiLauncher.Features.Mods;
using JiayiLauncher.Settings;
using JiayiLauncher.Utils;

namespace JiayiLauncher.Features.Game;

public static class Minecraft
{
	private static readonly List<Mod> _modsLoaded = new();

	public static List<Mod> ModsLoaded
	{
		get
		{
			if (!IsOpen) _modsLoaded.Clear();
			return _modsLoaded;
		}
	}
	
	public static bool IsOpen
	{
		get
		{
			var processes = Process.GetProcessesByName("Minecraft.Windows");
			if (processes.Length == 0) return false;
			Process = processes[0];
			return true;
		}
	}

	public static Process Process { get; private set; } = null!;

	public static async Task Open()
	{
		var minecraftApp = await PackageData.GetPackage();
		if (minecraftApp == null) return;
		await minecraftApp.LaunchAsync();
		
		Process = Process.GetProcessesByName("Minecraft.Windows")[0];
	}

	public static async Task WaitForModules()
	{
		await Task.Run(() =>
		{
			while (true)
			{
				Process.Refresh();
				if (JiayiSettings.Instance!.OverrideModuleRequirement)
					if (Process.Modules.Count > JiayiSettings.Instance.ModuleRequirement[2]) break;
				
				if (Process.Modules.Count > 160) break;

				if (JiayiSettings.Instance.AccelerateGameLoading)
				{
					var brokers = Process.GetProcessesByName("RuntimeBroker");
					if (brokers.Length > 0)
					{
						foreach (var broker in brokers)
						{
							broker.Kill();
						}
					}
				}

				// wait for a bit
				Task.Delay(100).Wait();
			}
		});
	}

	public static async Task<bool> ModSupported(Mod mod)
	{
		var version = await PackageData.GetVersion();
		Log.Write(nameof(Minecraft), $"Current game version is {version} and mod supports {string.Join(", ", mod.SupportedVersions)}");
		return mod.SupportedVersions.Contains(version) || mod.SupportedVersions.Contains("any version");
	}
}