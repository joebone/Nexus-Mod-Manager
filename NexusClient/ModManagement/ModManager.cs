﻿using System;
using System.Collections.Generic;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.ActivityMonitoring;
using Nexus.Client.ModRepositories;
using System.Diagnostics;
using System.Threading;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// The class the encapsulates managing mods.
	/// </summary>
	/// <remarks>
	/// The list of managed mods needs to be centralized to ensure integrity; having multiple mod managers, each
	/// with a potentially different list of managed mods, would be disastrous. As such, this
	/// object is a singleton to help enforce that policy.
	/// Note, however, that the singleton nature of the manager is not meant to provide global access to the object.
	/// As such, there is no static accessor to retrieve the singleton instance. Instead, the
	/// <see cref="Initialize"/> method returns the only instance that should be used.
	/// </remarks>
	public partial class ModManager
	{
		#region Singleton

		private static ModManager m_mmgCurrent = null;

		/// <summary>
		/// Initializes the singleton intances of the mod manager.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		/// <param name="p_amrMonitor">The activity monitor to use to track task progress.</param>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_mrgModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgInstallLog">The install log tracking mod activations for the current game mode.</param>
		/// <param name="p_pmgPluginManager">The plugin manager to use to work with plugins.</param>
		/// <returns>The initialized mod manager.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the mod manager has already
		/// been initialized.</exception>
		public static ModManager Initialize(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IModRepository p_mrpModRepository, ActivityMonitor p_amrMonitor, IModFormatRegistry p_frgFormatRegistry, ModRegistry p_mrgModRegistry, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgInstallLog, IPluginManager p_pmgPluginManager)
		{
			if (m_mmgCurrent != null)
				throw new InvalidOperationException("The Mod Manager has already been initialized.");
			m_mmgCurrent = new ModManager(p_gmdGameMode, p_eifEnvironmentInfo, p_mrpModRepository, p_amrMonitor, p_frgFormatRegistry, p_mrgModRegistry, p_futFileUtility, p_scxUIContext, p_ilgInstallLog, p_pmgPluginManager);
			return m_mmgCurrent;
		}

		/// <summary>
		/// This disposes of the singleton object, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
			ModAdditionQueue.Dispose();
			ModAdditionQueue = null;
			m_mmgCurrent = null;
		}

		#endregion

		private ModActivator m_macModActivator = null;

		#region Properties

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		protected IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the mod auto updater.
		/// </summary>
		/// <value>The mod auto updater.</value>
		protected AutoUpdater AutoUpdater { get; private set; }

		/// <summary>
		/// Gets the mod auto updater.
		/// </summary>
		/// <value>The mod auto updater.</value>
		protected ModActivator Activator
		{
			get
			{
				return m_macModActivator ?? (m_macModActivator = new ModActivator(InstallationLog, InstallerFactory));
			}
		}

		/// <summary>
		/// Gets the activity monitor to use to display status.
		/// </summary>
		/// <value>The activity monitor to use to display status.</value>
		protected ActivityMonitor ActivityMonitor { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModInstallerFactory"/> to use to create
		/// <see cref="ModInstaller"/>s.
		/// </summary>
		/// <value>The <see cref="ModInstallerFactory"/> to use to create
		/// <see cref="ModInstaller"/>s.</value>
		protected ModInstallerFactory InstallerFactory { get; private set; }

		/// <summary>
		/// Gets the install log tracking mod activations for the current game mode.
		/// </summary>
		/// <value>The install log tracking mod activations for the current game mode.</value>
		protected IInstallLog InstallationLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.
		/// </summary>
		/// <value>The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</value>
		protected IModFormatRegistry FormatRegistry { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.
		/// </summary>
		/// <value>The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</value>
		protected ModRegistry ManagedModRegistry { get; private set; }

		/// <summary>
		/// Gets the <see cref="AddModQueue"/> that contains the list
		/// of <see cref="IMod"/>s to be added to the mod manager.
		/// </summary>
		/// <value>The <see cref="AddModQueue"/> that contains the list
		/// of <see cref="IMod"/>s to be added to the mod manager.</value>
		protected AddModQueue ModAdditionQueue { get; private set; }

		/// <summary>
		/// Gets the newest available information about the managed mods.
		/// </summary>
		/// <value>The newest available information about the managed mods.</value>
		public ReadOnlyObservableList<AutoUpdater.UpdateInfo> NewestModInfo
		{
			get
			{
				return AutoUpdater.NewestModInfo;
			}
		}

		/// <summary>
		/// Gets the list of supported mod formats.
		/// </summary>
		/// <value>The list of supported mod formats.</value>
		public ICollection<IModFormat> ModFormats
		{
			get
			{
				return FormatRegistry.Formats;
			}
		}

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<IMod> ManagedMods
		{
			get
			{
				return ManagedModRegistry.RegisteredMods;
			}
		}

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<IMod> ActiveMods
		{
			get
			{
				return InstallationLog.ActiveMods;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		/// <param name="p_amrMonitor">The activity monitor to use to track task progress.</param>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_mdrManagedModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgInstallLog">The install log tracking mod activations for the current game mode.</param>
		/// <param name="p_pmgPluginManager">The plugin manager to use to work with plugins.</param>
		private ModManager(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IModRepository p_mrpModRepository, ActivityMonitor p_amrMonitor, IModFormatRegistry p_frgFormatRegistry, ModRegistry p_mdrManagedModRegistry, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgInstallLog, IPluginManager p_pmgPluginManager)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			ModRepository = p_mrpModRepository;
			FormatRegistry = p_frgFormatRegistry;
			ManagedModRegistry = p_mdrManagedModRegistry;
			InstallationLog = p_ilgInstallLog;
			InstallerFactory = new ModInstallerFactory(p_gmdGameMode, p_eifEnvironmentInfo, p_futFileUtility, p_scxUIContext, p_ilgInstallLog, p_pmgPluginManager);
			ActivityMonitor = p_amrMonitor;
			ModAdditionQueue = new AddModQueue(p_eifEnvironmentInfo, this);
			AutoUpdater = new AutoUpdater(p_mrpModRepository, p_mdrManagedModRegistry, p_eifEnvironmentInfo);
			if (EnvironmentInfo.Settings.CheckForNewModVersions)
				AutoUpdater.CheckForUpdates();
		}

		#endregion

		#region Mod Addition

		/// <summary>
		/// Installs the specified mod.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask AddMod(string p_strPath, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			Uri uriPath = new Uri(p_strPath);
			return ModAdditionQueue.AddMod(uriPath, p_cocConfirmOverwrite);
		}

		/// <summary>
		/// Loads the list of mods that are queued to be added to the mod manager.
		/// </summary>
		public void LoadQueuedMods()
		{
			ModAdditionQueue.LoadQueuedMods();
		}

		#endregion

		#region Mod Removal

		/// <summary>
		/// Deletes the given mod.
		/// </summary>
		/// <remarks>
		/// The mod is deactivated, unregistered, and then deleted.
		/// </remarks>
		/// <param name="p_modMod">The mod to delete.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation,
		/// or <c>null</c> if no long-running operation needs to be done.</returns>
		public IBackgroundTaskSet DeleteMod(IMod p_modMod)
		{
			ModDeleter mddDeleter = InstallerFactory.CreateDelete(p_modMod);
			mddDeleter.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(Deactivator_TaskSetCompleted);
			mddDeleter.Install();
			return mddDeleter;
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event of the mod deletion
		/// mod deativator.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void Deactivator_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			if (e.Success)
				ManagedModRegistry.UnregisterMod((IMod)e.ReturnValue);
		}

		#endregion

		#region Mod Activation/Deactivation

		/// <summary>
		/// Activates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to activate.</param>
		/// <param name="p_dlgUpgradeConfirmationDelegate">The delegate that is called to confirm whether an upgrade install should be performed.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ActivateMod(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			if (InstallationLog.ActiveMods.Contains(p_modMod))
				return null;
			return Activator.Activate(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Forces an upgrade from one mod to another.
		/// </summary>
		/// <remarks>
		/// No checks as to whether the two mods are actually related are performed. The new mod is reactivated
		/// as if it were the old mod, and the old mod is replaced by the new mod.
		/// </remarks>
		/// <param name="p_modOldMod">The mod from which to upgrade.</param>
		/// <param name="p_modNewMod">The mod to which to upgrade.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ForceUpgrade(IMod p_modOldMod, IMod p_modNewMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return Activator.ForceUpgrade(p_modOldMod, p_modNewMod, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Reactivates the given mod.
		/// </summary>
		/// <remarks>
		/// A reactivation is an upgrade of a mod to itself. It re-runs the activation,
		/// without changing the installed precedence of its files and installed values.
		/// </remarks>
		/// <param name="p_modMod">The mod to reactivate.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ReactivateMod(IMod p_modMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			if (!InstallationLog.ActiveMods.Contains(p_modMod))
				throw new InvalidOperationException(String.Format("Cannot reactivate the given mod, {0}. It is not active.", p_modMod.ModName));
			ModActivator marActivator = new ModActivator(InstallationLog, InstallerFactory);
			return marActivator.Reactivate(p_modMod, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// deactivates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to deactivate.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet DeactivateMod(IMod p_modMod)
		{
			if (!InstallationLog.ActiveMods.Contains(p_modMod))
				return null;
			ModUninstaller munUninstaller = InstallerFactory.CreateUninstaller(p_modMod);
			munUninstaller.Install();
			return munUninstaller;
		}

		#endregion

		#region Mod Tagging

		/// <summary>
		/// Gets the tagger to use to tag mods with metadata.
		/// </summary>
		/// <returns>The tagger to use to tag mods with metadata.</returns>
		public AutoTagger GetModTagger()
		{
			return new AutoTagger(ModRepository);
		}

		#endregion
	}
}
