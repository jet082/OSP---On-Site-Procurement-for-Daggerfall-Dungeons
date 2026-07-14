using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Save;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace OnSiteProcurementMod
{
	public class OnSiteProcurement : MonoBehaviour, IHasModSaveData
	{
		const string LessonNamePrefix = "Learn Spell - ";
		const string TempSpellTag = "OSP";
		const string StartingSpellTag = "OSP_START";
		const string EnteringText = "Entering Dungeon - Depositing Items/Spells";
		const string ExitingText = "Exiting Dungeon - Restoring Items/Spells";
		const int LessonMarker = 0x05F05F;
		const int LessonMinLevel = 1;
		const int LessonMaxLevel = 31;
		const int InitialLootPlacementDelayFrames = 8;
		const double ExtraSpellbookChance = 0.08;
		const int RandomOption = -1;
		const int CustomClassOption = -2;
		const int RandomLevelOption = 0;
		const int LevelDistributionRandom = 0;
		const int LevelDistributionPlayerChosen = 1;
		const int SkillGrowthDefaultWeights = 0;
		const int SkillGrowthCustomWeights = 1;
		const int SkillGrowthCompletelyRandom = 2;

		static Mod mod;
		static OnSiteProcurement instance;
		static readonly MethodInfo StartEquippedItemMethod = typeof(ItemEquipTable).GetMethod("StartEquippedItem", BindingFlags.Instance | BindingFlags.NonPublic);
		static readonly FieldInfo CharacterSheetStatsRolloutField = typeof(DaggerfallCharacterSheetWindow).GetField("statsRollout", BindingFlags.Instance | BindingFlags.NonPublic);
		static readonly BindingFlags ItemCollectionFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		static readonly Races[] PlayableRaces = {
			Races.Breton,
			Races.Redguard,
			Races.Nord,
			Races.DarkElf,
			Races.HighElf,
			Races.WoodElf,
			Races.Khajiit,
			Races.Argonian
		};
		static readonly string[] PlayableRaceNames = {
			"Breton",
			"Redguard",
			"Nord",
			"Dark Elf",
			"High Elf",
			"Wood Elf",
			"Khajiit",
			"Argonian"
		};
		static readonly ClassCareers[] PlayableClassCareers = {
			ClassCareers.Mage,
			ClassCareers.Spellsword,
			ClassCareers.Battlemage,
			ClassCareers.Sorcerer,
			ClassCareers.Healer,
			ClassCareers.Nightblade,
			ClassCareers.Bard,
			ClassCareers.Burglar,
			ClassCareers.Rogue,
			ClassCareers.Acrobat,
			ClassCareers.Thief,
			ClassCareers.Assassin,
			ClassCareers.Monk,
			ClassCareers.Archer,
			ClassCareers.Ranger,
			ClassCareers.Barbarian,
			ClassCareers.Warrior,
			ClassCareers.Knight
		};
		static readonly DFCareer.Skills[] MartialSkills = {
			DFCareer.Skills.ShortBlade,
			DFCareer.Skills.LongBlade,
			DFCareer.Skills.HandToHand,
			DFCareer.Skills.Axe,
			DFCareer.Skills.BluntWeapon,
			DFCareer.Skills.Archery
		};
		static readonly DFCareer.Skills[] WeaponPathSkills = {
			DFCareer.Skills.ShortBlade,
			DFCareer.Skills.LongBlade,
			DFCareer.Skills.Axe,
			DFCareer.Skills.BluntWeapon,
			DFCareer.Skills.Archery
		};
		static readonly DFCareer.Skills[] MagicSkills = {
			DFCareer.Skills.Alteration,
			DFCareer.Skills.Restoration,
			DFCareer.Skills.Destruction,
			DFCareer.Skills.Mysticism,
			DFCareer.Skills.Thaumaturgy,
			DFCareer.Skills.Illusion
		};
		static readonly MensClothing[] BasicMensTops = {
			MensClothing.Short_shirt,
			MensClothing.Short_shirt_with_belt,
			MensClothing.Long_shirt,
			MensClothing.Long_shirt_with_belt,
			MensClothing.Short_shirt_closed_top,
			MensClothing.Short_shirt_closed_top2,
			MensClothing.Long_shirt_closed_top,
			MensClothing.Long_shirt_closed_top2,
			MensClothing.Vest
		};
		static readonly MensClothing[] BasicMensBottoms = {
			MensClothing.Casual_pants,
			MensClothing.Breeches,
			MensClothing.Loincloth,
			MensClothing.Wrap
		};
		static readonly WomensClothing[] BasicWomensTops = {
			WomensClothing.Peasant_blouse,
			WomensClothing.Short_shirt,
			WomensClothing.Short_shirt_belt,
			WomensClothing.Long_shirt,
			WomensClothing.Long_shirt_belt,
			WomensClothing.Short_shirt_closed,
			WomensClothing.Short_shirt_closed_belt,
			WomensClothing.Long_shirt_closed,
			WomensClothing.Long_shirt_closed_belt,
			WomensClothing.Vest
		};
		static readonly WomensClothing[] BasicWomensBottoms = {
			WomensClothing.Casual_pants,
			WomensClothing.Loincloth,
			WomensClothing.Wrap,
			WomensClothing.Long_skirt,
			WomensClothing.Tights
		};
		OSPData data = new OSPData();
		bool entryPending;
		bool initialLootPlacementPending;
		int initialLootPlacementFrames;
		bool ospEnabled = true;
		bool applyInStartingDungeon = true;
		bool grantTemporarySpellbookOnEntry;
		bool keepLearnedSpellsOnExit;
		bool pureChaosMode;
		bool trueOSPMode;
		bool guaranteedPathToKillAllEnemies = true;
		bool dungeonSpoilerLog;
		bool enemyDefeatNotifications = true;
		bool startingDungeonEntryPending;
		bool titleButtonPending;
		bool startingDelegatesSuppressed;
		int queuedPlayerChosenLevelTarget;
		int queuedPlayerChosenLevelDelay;
		int queuedPlayerChosenBonusPool;
		bool queuedPlayerChosenBonusPoolApplied;
		DaggerfallStartWindow titleButtonWindow;
		RandomRunRequest pendingRandomRun;
		EventHandler suspendedRandomStartingDungeonHandler;
		StartGameBehaviour.PlayerStartingEquipment previousStartingEquipment;
		StartGameBehaviour.PlayerStartingSpells previousStartingSpells;
		System.Random lessonRandom = new System.Random();

		public Type SaveDataType { get { return typeof(OSPData); } }

		[Invoke(StateManager.StateTypes.Start, 200)]
		public static void Init(InitParams initParams)
		{
			mod = initParams.Mod;
			GameObject go = new GameObject(mod.Title);
			DontDestroyOnLoad(go);
			instance = go.AddComponent<OnSiteProcurement>();
			mod.SaveDataInterface = instance;
			mod.LoadSettingsCallback = instance.LoadSettings;
			instance.LoadSettings();
			DaggerfallUnity.Instance.ItemHelper.RegisterItemUseHandler((int)UselessItems2.Parchment, instance.UseLesson);
			mod.IsReady = true;
		}

		void LoadSettings()
		{
			if (mod.HasSettings)
				ApplySettings(mod.GetSettings());
		}

		void LoadSettings(ModSettings settings, ModSettingsChange change)
		{
			bool wasEnabled = ospEnabled;
			bool wasGuaranteedPath = guaranteedPathToKillAllEnemies;
			ApplySettings(settings);
			if (wasEnabled && !ospEnabled && data != null && data.Active)
				FinishRun();
			if (!wasGuaranteedPath && guaranteedPathToKillAllEnemies && data != null && data.Active && !data.KillPathWeaponSatisfied)
				QueueInitialLootPlacement();
		}

		void ApplySettings(ModSettings settings)
		{
			ospEnabled = settings.GetBool("General", "Enabled");
			applyInStartingDungeon = settings.GetBool("General", "ApplyInStartingDungeon");
			grantTemporarySpellbookOnEntry = settings.GetBool("General", "GrantTemporarySpellbookOnEntry");
			keepLearnedSpellsOnExit = settings.GetBool("General", "KeepLearnedSpellsOnExit");
			pureChaosMode = settings.GetBool("General", "PureChaosMode");
			trueOSPMode = settings.GetBool("General", "TrueOSPMode");
			guaranteedPathToKillAllEnemies = settings.GetBool("General", "GuaranteedPathToKillAllEnemies");
			dungeonSpoilerLog = settings.GetBool("General", "DungeonSpoilerLog");
			enemyDefeatNotifications = settings.GetBool("General", "EnemyDefeatNotifications");
		}

		void OnEnable()
		{
			PlayerEnterExit.OnPreTransition += OnPreTransition;
			PlayerEnterExit.OnFailedTransition += OnFailedTransition;
			PlayerEnterExit.OnTransitionDungeonInterior += OnTransitionDungeonInterior;
			PlayerEnterExit.OnTransitionDungeonExterior += OnTransitionDungeonExterior;
			EnemyDeath.OnEnemyDeath += OnEnemyDeath;
			StartGameBehaviour.OnNewGame += OnNewGame;
			StartGameBehaviour.OnStartGame += OnStartGame;
			DaggerfallStartWindow.OnStartFirstVisible += AddTitleButton;
			SaveLoadManager.OnLoad += OnLoad;
		}

		void OnDisable()
		{
			PlayerEnterExit.OnPreTransition -= OnPreTransition;
			PlayerEnterExit.OnFailedTransition -= OnFailedTransition;
			PlayerEnterExit.OnTransitionDungeonInterior -= OnTransitionDungeonInterior;
			PlayerEnterExit.OnTransitionDungeonExterior -= OnTransitionDungeonExterior;
			EnemyDeath.OnEnemyDeath -= OnEnemyDeath;
			StartGameBehaviour.OnNewGame -= OnNewGame;
			StartGameBehaviour.OnStartGame -= OnStartGame;
			DaggerfallStartWindow.OnStartFirstVisible -= AddTitleButton;
			SaveLoadManager.OnLoad -= OnLoad;
			RestoreRandomStartingDungeon();
		}

		void Update()
		{
			if (titleButtonPending)
				TryAddTitleButton();

			if (data == null || !data.Active)
				return;

			GameManager gameManager = GameManager.Instance;
			PlayerEnterExit playerEnterExit = gameManager != null ? gameManager.PlayerEnterExit : null;
			if (playerEnterExit != null && !playerEnterExit.IsPlayerInsideDungeon)
			{
				FinishRun();
				return;
			}

			UpdateQueuedPlayerChosenLevelUp();
			UpdateInitialLootPlacement();
		}

		public object NewSaveData()
		{
			return new OSPData();
		}

		public object GetSaveData()
		{
			return data != null && data.Active ? data : null;
		}

		public void RestoreSaveData(object saveData)
		{
			data = saveData as OSPData;
			if (data == null)
				data = new OSPData();
			entryPending = false;
			initialLootPlacementPending = data.Active;
			initialLootPlacementFrames = InitialLootPlacementDelayFrames;
			startingDungeonEntryPending = false;
			pendingRandomRun = null;
		}

		void OnNewGame()
		{
			StartGameBehaviour startGame = GameManager.Instance.StartGameBehaviour;
			if (pendingRandomRun != null)
			{
				DaggerfallUnity.Settings.StartCellX = pendingRandomRun.StartCellX;
				DaggerfallUnity.Settings.StartCellY = pendingRandomRun.StartCellY;
				DaggerfallUnity.Settings.StartInDungeon = true;
				CloseTopMessageBoxes();
			}

			startingDungeonEntryPending = startGame != null &&
				startGame.StartMethod == StartGameBehaviour.StartMethods.NewCharacter &&
				DaggerfallUnity.Settings.StartInDungeon;
			if (ospEnabled && startingDungeonEntryPending && (applyInStartingDungeon || pendingRandomRun != null))
				SuppressStartingDelegates(startGame);
		}

		void OnStartGame(object sender, EventArgs e)
		{
			ApplyPendingRandomRunLevel();
			if (pendingRandomRun != null)
			{
				RemoveRandomRunIntroQuests();
				if (pendingRandomRun.PlayerChosenLevelDistribution && pendingRandomRun.Level > 1)
				{
					queuedPlayerChosenLevelTarget = pendingRandomRun.Level;
					queuedPlayerChosenLevelDelay = 4;
					queuedPlayerChosenBonusPool = pendingRandomRun.PlayerChosenBonusPool;
					queuedPlayerChosenBonusPoolApplied = false;
				}
			}
			RestoreStartingDelegates();
			RestoreRandomRunStartSettings();
			RestoreRandomStartingDungeon();
			pendingRandomRun = null;
		}

		void AddTitleButton()
		{
			titleButtonPending = true;
			TryAddTitleButton();
		}

		void TryAddTitleButton()
		{
			IUserInterfaceWindow window = DaggerfallUI.UIManager.TopWindow;
			DaggerfallPopupWindow popup;
			while ((popup = window as DaggerfallPopupWindow) != null && popup.PreviousWindow != null)
				window = popup.PreviousWindow;

			DaggerfallStartWindow start = window as DaggerfallStartWindow;
			if (start == null)
				return;

			if (titleButtonWindow != start)
			{
				Button button = DaggerfallUI.AddTextButton(new Rect(96, 160, 128, 15), "OSP Quickstart", start.NativePanel);
				button.BackgroundColor = new Color32(35, 20, 8, 220);
				button.Outline.Color = new Color32(214, 176, 82, 255);
				button.Label.TextColor = new Color32(255, 230, 130, 255);
				button.Label.ShadowColor = Color.black;
				button.Label.TextScale = 0.8f;
				button.OnMouseClick += RandomDungeonButton_OnMouseClick;
				titleButtonWindow = start;
			}

			titleButtonPending = false;
		}

		void RandomDungeonButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
		{
			IUserInterfaceManager ui = DaggerfallUI.UIManager;
			ui.PushWindow(new RandomOSPDungeonWindow(ui, ui.TopWindow, this));
		}

		bool StartRandomOSPDungeon(RandomDungeonSelection selection)
		{
			if (!ospEnabled)
			{
				DaggerfallUI.MessageBox("OSP is disabled in mod settings.");
				return false;
			}

			System.Random random = new System.Random(Guid.NewGuid().GetHashCode());
			DFLocation location;
			DFPosition mapPixel;
			if (!TryPickRandomDungeon(random, selection.RegionIndex, out location, out mapPixel))
			{
				DaggerfallUI.MessageBox("No valid random dungeon could be found.");
				return false;
			}

			int level;
			CharacterDocument character = CreateRandomCharacterDocument(selection, random, out level);
			StartGameBehaviour startGame = GameManager.Instance.StartGameBehaviour;
			if (startGame == null)
			{
				DaggerfallUI.MessageBox("Could not start a random OSP dungeon.");
				return false;
			}

			pendingRandomRun = new RandomRunRequest();
			pendingRandomRun.Level = level;
			pendingRandomRun.StartCellX = mapPixel.X;
			pendingRandomRun.StartCellY = mapPixel.Y;
			pendingRandomRun.PreviousStartCellX = DaggerfallUnity.Settings.StartCellX;
			pendingRandomRun.PreviousStartCellY = DaggerfallUnity.Settings.StartCellY;
			pendingRandomRun.PreviousStartInDungeon = DaggerfallUnity.Settings.StartInDungeon;
			pendingRandomRun.PlayerChosenLevelDistribution = selection.LevelDistribution == LevelDistributionPlayerChosen;
			pendingRandomRun.PlayerChosenBonusPool = RollLevelUpBonusPool(level - 1, random);

			DaggerfallUnity.Settings.StartCellX = mapPixel.X;
			DaggerfallUnity.Settings.StartCellY = mapPixel.Y;
			DaggerfallUnity.Settings.StartInDungeon = true;
			startGame.CharacterDocument = character;
			SuspendRandomStartingDungeon();
			StartGameBehaviour.OnNewGame -= OnNewGame;
			StartGameBehaviour.OnNewGame += OnNewGame;
			startGame.StartMethod = StartGameBehaviour.StartMethods.NewCharacter;

			Debug.Log("[OSP] Starting random OSP dungeon: " + location.RegionName + "/" + location.Name + ", " + character.raceTemplate.Name + " " + character.gender + " " + character.career.Name + ", level " + level);
			return true;
		}

		void SuppressStartingDelegates(StartGameBehaviour startGame)
		{
			if (startGame == null || startingDelegatesSuppressed)
				return;

			previousStartingEquipment = startGame.AssignStartingEquipment;
			previousStartingSpells = startGame.AssignStartingSpells;
			startGame.AssignStartingEquipment = NoStartingEquipment;
			startGame.AssignStartingSpells = NoStartingSpells;
			startingDelegatesSuppressed = true;
		}

		void RestoreStartingDelegates()
		{
			if (!startingDelegatesSuppressed)
				return;

			StartGameBehaviour startGame = GameManager.Instance.StartGameBehaviour;
			if (startGame != null)
			{
				startGame.AssignStartingEquipment = previousStartingEquipment;
				startGame.AssignStartingSpells = previousStartingSpells;
			}

			previousStartingEquipment = null;
			previousStartingSpells = null;
			startingDelegatesSuppressed = false;
		}

		void ApplyPendingRandomRunLevel()
		{
			if (pendingRandomRun == null || pendingRandomRun.LevelApplied)
				return;

			PlayerEntity player = GameManager.Instance.PlayerEntity;
			if (player == null)
				return;

			player.Level = pendingRandomRun.PlayerChosenLevelDistribution ? Math.Max(1, pendingRandomRun.Level - 1) : pendingRandomRun.Level;
			player.MaxHealth = FormulaHelper.RollMaxHealth(player);
			player.FillVitalSigns();
			player.SetCurrentLevelUpSkillSum();
			player.StartingLevelUpSkillSum = player.CurrentLevelUpSkillSum;
			player.ReadyToLevelUp = false;
			pendingRandomRun.LevelApplied = true;
		}

		void UpdateQueuedPlayerChosenLevelUp()
		{
			if (queuedPlayerChosenLevelTarget <= 1)
				return;

			PlayerEntity player = GameManager.Instance.PlayerEntity;
			if (player == null)
				return;

			DaggerfallCharacterSheetWindow characterSheet = DaggerfallUI.UIManager.TopWindow as DaggerfallCharacterSheetWindow;
			if (characterSheet != null && !queuedPlayerChosenBonusPoolApplied && !player.ReadyToLevelUp && ApplyQueuedBonusPool(characterSheet))
			{
				queuedPlayerChosenBonusPoolApplied = true;
				return;
			}

			if (player.Level >= queuedPlayerChosenLevelTarget)
			{
				queuedPlayerChosenLevelTarget = 0;
				return;
			}

			DaggerfallUI ui = DaggerfallUI.Instance;
			if (ui == null || DaggerfallUI.UIManager.TopWindow != ui.DaggerfallHUD)
				return;

			if (queuedPlayerChosenLevelDelay > 0)
			{
				queuedPlayerChosenLevelDelay--;
				return;
			}

			player.Level = Math.Max(1, queuedPlayerChosenLevelTarget - 1);
			player.MaxHealth = FormulaHelper.RollMaxHealth(player);
			player.FillVitalSigns();
			player.ReadyToLevelUp = true;
			DaggerfallUI.PostMessage(DaggerfallUIMessages.dfuiOpenCharacterSheetWindow);
		}

		bool ApplyQueuedBonusPool(DaggerfallCharacterSheetWindow characterSheet)
		{
			if (CharacterSheetStatsRolloutField == null)
				return false;

			StatsRollout statsRollout = CharacterSheetStatsRolloutField.GetValue(characterSheet) as StatsRollout;
			if (statsRollout == null || statsRollout.BonusPool <= 0)
				return false;

			statsRollout.BonusPool = queuedPlayerChosenBonusPool;
			return true;
		}

		int RollLevelUpBonusPool(int levelUps, System.Random random)
		{
			int total = 0;
			for (int i = 0; i < levelUps; i++)
				total += random.Next(4, 7);
			return total;
		}

		void RestoreRandomRunStartSettings()
		{
			if (pendingRandomRun == null)
				return;

			DaggerfallUnity.Settings.StartCellX = pendingRandomRun.PreviousStartCellX;
			DaggerfallUnity.Settings.StartCellY = pendingRandomRun.PreviousStartCellY;
			DaggerfallUnity.Settings.StartInDungeon = pendingRandomRun.PreviousStartInDungeon;
		}

		void SuspendRandomStartingDungeon()
		{
			if (suspendedRandomStartingDungeonHandler != null || StartGameBehaviour.OnStartGame == null)
				return;

			Delegate[] handlers = StartGameBehaviour.OnStartGame.GetInvocationList();
			for (int i = 0; i < handlers.Length; i++)
			{
				EventHandler handler = handlers[i] as EventHandler;
				if (handler == null || handler.Method == null || handler.Method.DeclaringType == null)
					continue;

				if (handler.Method.Name == "RandomizeSpawn_OnStartGame" &&
					handler.Method.DeclaringType.FullName == "RandomStartingDungeon.RandomStartingDungeonMain")
				{
					StartGameBehaviour.OnStartGame -= handler;
					suspendedRandomStartingDungeonHandler = handler;
					Debug.Log("[OSP] Temporarily suspended Random Starting Dungeon for OSP Quickstart.");
					return;
				}
			}
		}

		void RestoreRandomStartingDungeon()
		{
			if (suspendedRandomStartingDungeonHandler == null)
				return;

			StartGameBehaviour.OnStartGame += suspendedRandomStartingDungeonHandler;
			suspendedRandomStartingDungeonHandler = null;
			Debug.Log("[OSP] Restored Random Starting Dungeon start handler.");
		}

		static void NoStartingEquipment(PlayerEntity playerEntity, CharacterDocument characterDocument)
		{
		}

		static void NoStartingSpells(PlayerEntity playerEntity, CharacterDocument characterDocument)
		{
		}

		void RemoveRandomRunIntroQuests()
		{
			RemoveQuestByName("_TUTOR__");
			RemoveQuestByName("_BRISIEN");
			RemoveQuestByName("S0000977");
			RemoveQuestByName("S0000999");
			CloseTopMessageBoxes();
		}

		void RemoveQuestByName(string questName)
		{
			ulong[] quests = QuestMachine.Instance.FindQuests(questName);
			for (int i = 0; i < quests.Length; i++)
				QuestMachine.Instance.RemoveQuest(quests[i]);
		}

		void CloseTopMessageBoxes()
		{
			IUserInterfaceManager ui = DaggerfallUI.UIManager;
			while (ui.TopWindow as DaggerfallMessageBox != null)
				ui.PopWindow();
		}

		List<int> GetDungeonRegionIndices()
		{
			MapsFile maps = DaggerfallUnity.Instance.ContentReader.MapFileReader;
			List<int> regions = new List<int>();
			for (int region = 0; region < maps.RegionCount; region++)
			{
				if (HasEligibleDungeonInRegion(maps, region))
					regions.Add(region);
			}

			return regions;
		}

		bool HasEligibleDungeonInRegion(MapsFile maps, int region)
		{
			DFRegion dfRegion = maps.GetRegion(region);
			if (dfRegion.MapTable == null)
				return false;

			for (int locationIndex = 0; locationIndex < (int)dfRegion.LocationCount; locationIndex++)
			{
				DFRegion.RegionMapTable mapTable = dfRegion.MapTable[locationIndex];
				if (mapTable.DungeonType == DFRegion.DungeonTypes.NoDungeon)
					continue;
				if (DaggerfallDungeon.IsMainStoryDungeon(mapTable.MapId))
					continue;

				return true;
			}

			return false;
		}

		string GetRegionDisplayName(int regionIndex)
		{
			MapsFile maps = DaggerfallUnity.Instance.ContentReader.MapFileReader;
			if (regionIndex < 0 || regionIndex >= maps.RegionCount)
				return "Random";

			string name = maps.GetRegionName(regionIndex);
			return string.IsNullOrEmpty(name) ? "Region " + regionIndex : name;
		}

		bool TryPickRandomDungeon(System.Random random, int regionFilter, out DFLocation location, out DFPosition mapPixel)
		{
			location = new DFLocation();
			mapPixel = new DFPosition();

			MapsFile maps = DaggerfallUnity.Instance.ContentReader.MapFileReader;
			List<DungeonStartCandidate> candidates = new List<DungeonStartCandidate>();
			int startRegion = regionFilter == RandomOption ? 0 : Mathf.Clamp(regionFilter, 0, maps.RegionCount - 1);
			int endRegion = regionFilter == RandomOption ? maps.RegionCount : startRegion + 1;
			for (int region = startRegion; region < endRegion; region++)
			{
				DFRegion dfRegion = maps.GetRegion(region);
				if (dfRegion.MapTable == null)
					continue;

				for (int locationIndex = 0; locationIndex < (int)dfRegion.LocationCount; locationIndex++)
				{
					DFRegion.RegionMapTable mapTable = dfRegion.MapTable[locationIndex];
					if (mapTable.DungeonType == DFRegion.DungeonTypes.NoDungeon)
						continue;
					if (DaggerfallDungeon.IsMainStoryDungeon(mapTable.MapId))
						continue;

					candidates.Add(new DungeonStartCandidate(region, locationIndex));
				}
			}

			while (candidates.Count > 0)
			{
				int index = random.Next(candidates.Count);
				DungeonStartCandidate candidate = candidates[index];
				candidates.RemoveAt(index);

				location = maps.GetLocation(candidate.RegionIndex, candidate.LocationIndex);
				if (!location.Loaded || !location.HasDungeon)
					continue;

				mapPixel = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
				return true;
			}

			return false;
		}

		CharacterDocument CreateRandomCharacterDocument(RandomDungeonSelection selection, System.Random random, out int level)
		{
			Races race = ResolveRace(selection, random);
			Genders gender = ResolveGender(selection, random);
			bool isCustom;
			int classIndex;
			DFCareer career = ResolveCareer(selection, random, out isCustom, out classIndex);
			level = ResolveLevel(selection, random);

			CharacterDocument document = new CharacterDocument();
			document.raceTemplate = CharacterDocument.GetRaceTemplate(race);
			document.gender = gender;
			document.career = career;
			document.name = "Random OSP";
			document.faceIndex = random.Next(0, 10);
			document.reflexes = PlayerReflexes.Average;
			document.classIndex = classIndex;
			document.isCustom = isCustom;
			document.biographyEffects = new List<string>();
			document.backStory = new List<string>();
			document.skillUses = new short[DaggerfallSkills.Count];
			for (int i = 0; i < document.armorValues.Length; i++)
				document.armorValues[i] = 100;
			if (isCustom)
			{
				document.reputationMerchants = selection.MerchantsRep;
				document.reputationCommoners = selection.PeasantsRep;
				document.reputationScholars = selection.ScholarsRep;
				document.reputationNobility = selection.NobilityRep;
				document.reputationUnderworld = selection.UnderworldRep;
			}

			document.workingStats.SetPermanentFromCareer(career);
			AllocateLevelStats(document.workingStats, career, selection.LevelDistribution == LevelDistributionPlayerChosen ? 1 : level, random);
			document.startingStats.Copy(document.workingStats);

			BuildStartingSkills(document.workingSkills, career, level, selection, random);
			document.startingSkills.Copy(document.workingSkills);
			return document;
		}

		Races ResolveRace(RandomDungeonSelection selection, System.Random random)
		{
			if (selection.RaceIndex == RandomOption)
				return PlayableRaces[random.Next(PlayableRaces.Length)];

			return PlayableRaces[Mathf.Clamp(selection.RaceIndex, 0, PlayableRaces.Length - 1)];
		}

		Genders ResolveGender(RandomDungeonSelection selection, System.Random random)
		{
			if (selection.GenderIndex == RandomOption)
				return random.Next(2) == 0 ? Genders.Male : Genders.Female;

			return selection.GenderIndex == 0 ? Genders.Male : Genders.Female;
		}

		int ResolveLevel(RandomDungeonSelection selection, System.Random random)
		{
			int maxLevel = GetMaxRandomDungeonLevel();
			if (selection.Level == RandomLevelOption)
				return random.Next(1, maxLevel + 1);

			return Mathf.Clamp(selection.Level, 1, maxLevel);
		}

		int GetMaxRandomDungeonLevel()
		{
			return FormulaHelper.MaxStatValue();
		}

		DFCareer ResolveCareer(RandomDungeonSelection selection, System.Random random, out bool isCustom, out int classIndex)
		{
			int selectedClass = selection.ClassIndex;
			if (selectedClass == RandomOption)
			{
				int roll = random.Next(PlayableClassCareers.Length + 1);
				selectedClass = roll == PlayableClassCareers.Length ? CustomClassOption : roll;
			}

			if (selectedClass == CustomClassOption)
			{
				isCustom = true;
				classIndex = -1;
				if (selection.CustomCareer != null)
					return CloneCareer(selection.CustomCareer);
				return CreateRandomCustomCareer(random);
			}

			selectedClass = Mathf.Clamp(selectedClass, 0, PlayableClassCareers.Length - 1);
			ClassCareers careerId = PlayableClassCareers[selectedClass];
			DFCareer career = DaggerfallEntity.GetClassCareerTemplate(careerId);
			if (career == null)
				career = DaggerfallEntity.GetClassCareerTemplate(ClassCareers.Warrior);

			isCustom = false;
			classIndex = (int)careerId;
			return career;
		}

		DFCareer CreateRandomCustomCareer(System.Random random)
		{
			DFCareer baseCareer = DaggerfallEntity.GetClassCareerTemplate(PlayableClassCareers[random.Next(PlayableClassCareers.Length)]);
			if (baseCareer == null)
				baseCareer = DaggerfallEntity.GetClassCareerTemplate(ClassCareers.Warrior);

			DFCareer career = CloneCareer(baseCareer);
			career.Name = "Custom";
			List<DFCareer.Skills> skills = BuildCustomSkillOrder(random);
			career.PrimarySkill1 = skills[0];
			career.PrimarySkill2 = skills[1];
			career.PrimarySkill3 = skills[2];
			career.MajorSkill1 = skills[3];
			career.MajorSkill2 = skills[4];
			career.MajorSkill3 = skills[5];
			career.MinorSkill1 = skills[6];
			career.MinorSkill2 = skills[7];
			career.MinorSkill3 = skills[8];
			career.MinorSkill4 = skills[9];
			career.MinorSkill5 = skills[10];
			career.MinorSkill6 = skills[11];
			career.HitPointsPerLevel = random.Next(8, 21);
			career.ForbiddenProficiencies = (DFCareer.ProficiencyFlags)0;
			career.ShortBlades = DFCareer.Proficiency.Normal;
			career.LongBlades = DFCareer.Proficiency.Normal;
			career.HandToHand = DFCareer.Proficiency.Normal;
			career.Axes = DFCareer.Proficiency.Normal;
			career.BluntWeapons = DFCareer.Proficiency.Normal;
			career.MissileWeapons = DFCareer.Proficiency.Normal;
			SetCustomSpellPoints(career, CountMagicSkillsInClass(career));
			return career;
		}

		DFCareer CloneCareer(DFCareer source)
		{
			DFCareer copy = new DFCareer();
			FieldInfo[] fields = typeof(DFCareer).GetFields(BindingFlags.Instance | BindingFlags.Public);
			for (int i = 0; i < fields.Length; i++)
				fields[i].SetValue(copy, fields[i].GetValue(source));
			return copy;
		}

		List<DFCareer.Skills> BuildCustomSkillOrder(System.Random random)
		{
			List<DFCareer.Skills> chosen = new List<DFCareer.Skills>();
			if (random.NextDouble() < 0.45)
				AddRandomUnique(chosen, MagicSkills, random);
			else
				AddRandomUnique(chosen, MartialSkills, random);

			if (random.NextDouble() < 0.45)
				AddRandomUnique(chosen, MartialSkills, random);
			if (random.NextDouble() < 0.45)
				AddRandomUnique(chosen, MagicSkills, random);

			List<DFCareer.Skills> allSkills = GetAllSkills();
			while (chosen.Count < 12)
				AddRandomUnique(chosen, allSkills, random);

			return chosen;
		}

		void AddRandomUnique(List<DFCareer.Skills> chosen, DFCareer.Skills[] source, System.Random random)
		{
			List<DFCareer.Skills> list = new List<DFCareer.Skills>(source);
			AddRandomUnique(chosen, list, random);
		}

		void AddRandomUnique(List<DFCareer.Skills> chosen, List<DFCareer.Skills> source, System.Random random)
		{
			if (source == null || source.Count == 0)
				return;

			for (int attempts = 0; attempts < source.Count * 2; attempts++)
			{
				DFCareer.Skills skill = source[random.Next(source.Count)];
				if (!chosen.Contains(skill))
				{
					chosen.Add(skill);
					return;
				}
			}

			for (int i = 0; i < source.Count; i++)
			{
				if (!chosen.Contains(source[i]))
				{
					chosen.Add(source[i]);
					return;
				}
			}
		}

		List<DFCareer.Skills> GetAllSkills()
		{
			List<DFCareer.Skills> skills = new List<DFCareer.Skills>();
			for (int i = 0; i < DaggerfallSkills.Count; i++)
				skills.Add((DFCareer.Skills)i);
			return skills;
		}

		void SetCustomSpellPoints(DFCareer career, int magicSkillCount)
		{
			if (magicSkillCount >= 4)
			{
				career.SpellPointMultiplier = DFCareer.SpellPointMultipliers.Times_2_00;
				career.SpellPointMultiplierValue = 2.0f;
			}
			else if (magicSkillCount >= 2)
			{
				career.SpellPointMultiplier = DFCareer.SpellPointMultipliers.Times_1_50;
				career.SpellPointMultiplierValue = 1.5f;
			}
			else
			{
				career.SpellPointMultiplier = DFCareer.SpellPointMultipliers.Times_1_00;
				career.SpellPointMultiplierValue = 1.0f;
			}
		}

		int CountMagicSkillsInClass(DFCareer career)
		{
			int count = 0;
			List<DFCareer.Skills> classSkills = GetCareerSkills(career);
			for (int i = 0; i < classSkills.Count; i++)
			{
				if (IsMagicSkill(classSkills[i]))
					count++;
			}

			return count;
		}

		void AllocateLevelStats(DaggerfallStats stats, DFCareer career, int level, System.Random random)
		{
			List<DFCareer.Skills> classSkills = GetCareerSkills(career);
			int points = RollLevelUpBonusPool(Math.Max(0, level - 1), random);
			for (int i = 0; i < points; i++)
			{
				DFCareer.Stats stat;
				if (classSkills.Count > 0 && random.NextDouble() < 0.75)
					stat = PreferredStatForSkill(classSkills[random.Next(classSkills.Count)]);
				else
					stat = (DFCareer.Stats)random.Next(0, 8);

				if (!IncreaseStat(stats, stat) && !IncreaseRandomOpenStat(stats, random))
					return;
			}
		}

		bool IncreaseStat(DaggerfallStats stats, DFCareer.Stats stat)
		{
			int value = stats.GetPermanentStatValue(stat);
			if (value >= 100)
				return false;

			stats.SetPermanentStatValue(stat, value + 1);
			return true;
		}

		bool IncreaseRandomOpenStat(DaggerfallStats stats, System.Random random)
		{
			int start = random.Next(0, DaggerfallStats.Count);
			for (int i = 0; i < DaggerfallStats.Count; i++)
			{
				DFCareer.Stats stat = (DFCareer.Stats)((start + i) % DaggerfallStats.Count);
				if (IncreaseStat(stats, stat))
					return true;
			}

			return false;
		}

		void BuildStartingSkills(DaggerfallSkills skills, DFCareer career, int level, RandomDungeonSelection selection, System.Random random)
		{
			for (int i = 0; i < DaggerfallSkills.Count; i++)
				skills.SetPermanentSkillValue(i, (short)random.Next(3, 7));

			DFCareer.Skills[] primary = GetPrimaryCareerSkills(career);
			DFCareer.Skills[] major = GetMajorCareerSkills(career);
			DFCareer.Skills[] minor = GetMinorCareerSkills(career);
			SetSkillBand(skills, primary, 28, random);
			SetSkillBand(skills, major, 18, random);
			SetSkillBand(skills, minor, 13, random);
			DistributeSkillPoints(skills, primary, 6, random);
			DistributeSkillPoints(skills, major, 6, random);
			DistributeSkillPoints(skills, minor, 6, random);

			int levelPoints = Math.Max(0, level - 1) * 15;
			if (selection.SkillGrowthMode == SkillGrowthCompletelyRandom)
			{
				for (int i = 0; i < levelPoints; i++)
					IncreaseSkill(skills, (DFCareer.Skills)random.Next(0, DaggerfallSkills.Count), 95);
				return;
			}

			List<DFCareer.Skills> weighted = new List<DFCareer.Skills>();
			AddWeightedSkills(weighted, primary, selection.SkillGrowthMode == SkillGrowthCustomWeights ? selection.PrimarySkillWeight : 5);
			AddWeightedSkills(weighted, major, selection.SkillGrowthMode == SkillGrowthCustomWeights ? selection.MajorSkillWeight : 3);
			AddWeightedSkills(weighted, minor, selection.SkillGrowthMode == SkillGrowthCustomWeights ? selection.MinorSkillWeight : 2);
			int randomChance = selection.SkillGrowthMode == SkillGrowthCustomWeights ? Mathf.Clamp(selection.RandomSkillChance, 0, 100) : 10;
			for (int i = 0; i < levelPoints; i++)
			{
				if (weighted.Count > 0 && random.Next(100) >= randomChance)
					IncreaseSkill(skills, weighted[random.Next(weighted.Count)], 95);
				else
					IncreaseSkill(skills, (DFCareer.Skills)random.Next(0, DaggerfallSkills.Count), 80);
			}
		}

		void SetSkillBand(DaggerfallSkills skills, DFCareer.Skills[] band, int baseValue, System.Random random)
		{
			for (int i = 0; i < band.Length; i++)
				skills.SetPermanentSkillValue(band[i], (short)(baseValue + random.Next(0, 4)));
		}

		void DistributeSkillPoints(DaggerfallSkills skills, DFCareer.Skills[] band, int points, System.Random random)
		{
			for (int i = 0; i < points; i++)
				IncreaseSkill(skills, band[random.Next(band.Length)], 95);
		}

		void AddWeightedSkills(List<DFCareer.Skills> weighted, DFCareer.Skills[] skills, int weight)
		{
			for (int i = 0; i < skills.Length; i++)
			{
				for (int j = 0; j < weight; j++)
					weighted.Add(skills[i]);
			}
		}

		void IncreaseSkill(DaggerfallSkills skills, DFCareer.Skills skill, int cap)
		{
			short value = skills.GetPermanentSkillValue(skill);
			if (value < cap)
				skills.SetPermanentSkillValue(skill, (short)(value + 1));
		}

		List<DFCareer.Skills> GetCareerSkills(DFCareer career)
		{
			List<DFCareer.Skills> skills = new List<DFCareer.Skills>();
			skills.AddRange(GetPrimaryCareerSkills(career));
			skills.AddRange(GetMajorCareerSkills(career));
			skills.AddRange(GetMinorCareerSkills(career));
			return skills;
		}

		DFCareer.Skills[] GetPrimaryCareerSkills(DFCareer career)
		{
			return new DFCareer.Skills[] {
				career.PrimarySkill1,
				career.PrimarySkill2,
				career.PrimarySkill3
			};
		}

		DFCareer.Skills[] GetMajorCareerSkills(DFCareer career)
		{
			return new DFCareer.Skills[] {
				career.MajorSkill1,
				career.MajorSkill2,
				career.MajorSkill3
			};
		}

		DFCareer.Skills[] GetMinorCareerSkills(DFCareer career)
		{
			return new DFCareer.Skills[] {
				career.MinorSkill1,
				career.MinorSkill2,
				career.MinorSkill3,
				career.MinorSkill4,
				career.MinorSkill5,
				career.MinorSkill6
			};
		}

		bool IsMagicSkill(DFCareer.Skills skill)
		{
			for (int i = 0; i < MagicSkills.Length; i++)
			{
				if (MagicSkills[i] == skill)
					return true;
			}

			return false;
		}

		DFCareer.Stats PreferredStatForSkill(DFCareer.Skills skill)
		{
			switch (skill)
			{
				case DFCareer.Skills.ShortBlade:
				case DFCareer.Skills.LongBlade:
				case DFCareer.Skills.Axe:
				case DFCareer.Skills.BluntWeapon:
				case DFCareer.Skills.HandToHand:
				case DFCareer.Skills.CriticalStrike:
					return DFCareer.Stats.Strength;
				case DFCareer.Skills.Archery:
				case DFCareer.Skills.Dodging:
				case DFCareer.Skills.Backstabbing:
				case DFCareer.Skills.Pickpocket:
				case DFCareer.Skills.Lockpicking:
					return DFCareer.Stats.Agility;
				case DFCareer.Skills.Running:
				case DFCareer.Skills.Jumping:
				case DFCareer.Skills.Climbing:
				case DFCareer.Skills.Swimming:
				case DFCareer.Skills.Stealth:
					return DFCareer.Stats.Speed;
				case DFCareer.Skills.Destruction:
				case DFCareer.Skills.Mysticism:
				case DFCareer.Skills.Thaumaturgy:
					return DFCareer.Stats.Intelligence;
				case DFCareer.Skills.Restoration:
				case DFCareer.Skills.Alteration:
				case DFCareer.Skills.Illusion:
				case DFCareer.Skills.Medical:
					return DFCareer.Stats.Willpower;
				case DFCareer.Skills.Etiquette:
				case DFCareer.Skills.Streetwise:
				case DFCareer.Skills.Mercantile:
					return DFCareer.Stats.Personality;
				default:
					return DFCareer.Stats.Luck;
			}
		}

		void OnLoad(SaveData_v1 saveData)
		{
			if (data == null || !data.Active)
				return;

			PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
			if (!playerEnterExit || playerEnterExit.IsPlayerInsideDungeon)
				return;

			startingDungeonEntryPending = false;
			FinishRun();
		}

		void OnPreTransition(PlayerEnterExit.TransitionEventArgs args)
		{
			if (!ospEnabled)
				return;
			if (args.TransitionType == PlayerEnterExit.TransitionType.ToDungeonExterior)
				return;
			if (args.TransitionType != PlayerEnterExit.TransitionType.ToDungeonInterior || data.Active || entryPending)
				return;
			if (startingDungeonEntryPending)
			{
				startingDungeonEntryPending = false;
				if (!applyInStartingDungeon && pendingRandomRun == null)
					return;
			}

			entryPending = true;
			initialLootPlacementPending = false;
			data = new OSPData();
			data.RandomSeed = BuildDungeonSeed(args != null ? args.DaggerfallDungeon : null);
			BeginStash();
		}

		void OnFailedTransition(PlayerEnterExit.TransitionEventArgs args)
		{
			if (args.TransitionType != PlayerEnterExit.TransitionType.ToDungeonInterior || !entryPending)
				return;

			RestoreStashedItems(GameManager.Instance.PlayerEntity);
			data = new OSPData();
			entryPending = false;
			initialLootPlacementPending = false;
			initialLootPlacementFrames = 0;
		}

		void OnTransitionDungeonInterior(PlayerEnterExit.TransitionEventArgs args)
		{
			if (!entryPending)
				return;

			ApplyPendingRandomRunLevel();
			PlayerEntity player = GameManager.Instance.PlayerEntity;
			data.Active = true;
			if (args != null && args.DaggerfallDungeon != null)
				data.RandomSeed = BuildDungeonSeed(args.DaggerfallDungeon);
			data.OriginalSpellbook = player.SerializeSpellbook();
			player.DeserializeSpellbook(null);
			InitializeEnemyDefeatCounters();
			DaggerfallUI.AddHUDText(EnteringText);

			if (!trueOSPMode)
			{
				RunWithOSPSeed(0x1000, delegate
				{
					GrantFieldKit(player);
					GrantStartingSpell(player);
				});
			}

			QueueInitialLootPlacement();
			entryPending = false;
		}

		void OnTransitionDungeonExterior(PlayerEnterExit.TransitionEventArgs args)
		{
			if (data == null || !data.Active)
				return;

			FinishRun();
		}

		void OnEnemyDeath(object sender, EventArgs e)
		{
			if (data == null || !data.Active)
				return;

			EnemyDeath enemyDeath = sender as EnemyDeath;
			if (enemyDeath == null)
				return;

			DaggerfallEntityBehaviour entityBehaviour = enemyDeath.GetComponent<DaggerfallEntityBehaviour>();
			if (entityBehaviour != null)
				TryPopulateLootContainer(entityBehaviour.CorpseLootContainer);

			RecordEnemyDefeat();
		}

		void InitializeEnemyDefeatCounters()
		{
			if (data == null)
				return;

			data.EnemiesDefeated = 0;
			data.EnemiesTotal = CountActiveEnemies();
		}

		void RecordEnemyDefeat()
		{
			if (data == null || !data.Active)
				return;

			if (data.EnemiesTotal <= 0)
				data.EnemiesTotal = data.EnemiesDefeated + CountActiveEnemies() + 1;

			data.EnemiesDefeated++;
			if (data.EnemiesDefeated > data.EnemiesTotal)
				data.EnemiesTotal = data.EnemiesDefeated;

			if (enemyDefeatNotifications)
				DaggerfallUI.AddHUDText("Enemies Defeated " + data.EnemiesDefeated + "/" + data.EnemiesTotal);
		}

		int CountActiveEnemies()
		{
			int count = 0;
			foreach (DaggerfallEntityBehaviour enemy in ActiveGameObjectDatabase.GetActiveEnemyBehaviours())
			{
				if (enemy != null && enemy.Entity != null)
					count++;
			}

			return count;
		}

		void BeginStash()
		{
			PlayerEntity player = GameManager.Instance.PlayerEntity;
			List<ulong> stashed = new List<ulong>();
			data.StashedGold = player.GoldPieces;
			data.OriginalEquipTable = player.ItemEquipTable.SerializeEquipTable();
			data.OriginalLightSourceUid = player.LightSource != null ? player.LightSource.UID : 0;

			UnequipAll(player);
			for (int i = player.Items.Count - 1; i >= 0; i--)
			{
				DaggerfallUnityItem item = player.Items.GetItem(i);
				stashed.Add(item.UID);
				player.OtherItems.Transfer(item, player.Items);
			}

			if (player.LightSource != null && !player.Items.Contains(player.LightSource))
				player.LightSource = null;

			player.GoldPieces = 0;
			data.StashedItemUids = stashed.ToArray();
		}

		void GrantFieldKit(PlayerEntity player)
		{
			List<ulong> issued = new List<ulong>();
			DaggerfallUnityItem shirt;
			DaggerfallUnityItem pants;

			if (player.Gender == Genders.Female)
			{
				shirt = ItemBuilder.CreateWomensClothing(BasicWomensTops[UnityEngine.Random.Range(0, BasicWomensTops.Length)], player.Race, -1, ItemBuilder.RandomClothingDye());
				pants = ItemBuilder.CreateWomensClothing(BasicWomensBottoms[UnityEngine.Random.Range(0, BasicWomensBottoms.Length)], player.Race, -1, ItemBuilder.RandomClothingDye());
			}
			else
			{
				shirt = ItemBuilder.CreateMensClothing(BasicMensTops[UnityEngine.Random.Range(0, BasicMensTops.Length)], player.Race, -1, ItemBuilder.RandomClothingDye());
				pants = ItemBuilder.CreateMensClothing(BasicMensBottoms[UnityEngine.Random.Range(0, BasicMensBottoms.Length)], player.Race, -1, ItemBuilder.RandomClothingDye());
			}

			AddAndEquipIssued(player, shirt, issued);
			AddAndEquipIssued(player, pants, issued);

			if (grantTemporarySpellbookOnEntry)
			{
				DaggerfallUnityItem spellbook = ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Spellbook);
				AddIssued(player, spellbook, issued);
				data.SpellbookInjected = spellbook != null;
			}

			switch (RunMartialSkill(player))
			{
				case DFCareer.Skills.HandToHand:
					AddIssued(player, CreateRandomLowestValuePotion(), issued);
					break;
				case DFCareer.Skills.LongBlade:
					AddAndEquipIssued(player, ItemBuilder.CreateWeapon(Weapons.Broadsword, WeaponMaterialTypes.Iron), issued);
					break;
				case DFCareer.Skills.Axe:
					AddAndEquipIssued(player, ItemBuilder.CreateWeapon(Weapons.Battle_Axe, WeaponMaterialTypes.Iron), issued);
					break;
				case DFCareer.Skills.BluntWeapon:
					AddAndEquipIssued(player, ItemBuilder.CreateWeapon(Weapons.Mace, WeaponMaterialTypes.Iron), issued);
					break;
				case DFCareer.Skills.Archery:
					AddAndEquipIssued(player, ItemBuilder.CreateWeapon(Weapons.Short_Bow, WeaponMaterialTypes.Iron), issued);
					AddIssued(player, ItemBuilder.CreateWeapon(Weapons.Arrow, WeaponMaterialTypes.Iron), issued);
					break;
				default:
					AddAndEquipIssued(player, ItemBuilder.CreateWeapon(Weapons.Dagger, WeaponMaterialTypes.Iron), issued);
					break;
			}

			data.IssuedItemUids = issued.ToArray();
		}

		DaggerfallUnityItem CreateRandomLowestValuePotion()
		{
			List<int> recipeKeys = GameManager.Instance.EntityEffectBroker.GetPotionRecipeKeys();
			List<int> cheapest = new List<int>();
			int bestPrice = int.MaxValue;
			for (int i = 0; i < recipeKeys.Count; i++)
			{
				PotionRecipe recipe = GameManager.Instance.EntityEffectBroker.GetPotionRecipe(recipeKeys[i]);
				if (recipe == null)
					continue;

				if (recipe.Price < bestPrice)
				{
					cheapest.Clear();
					bestPrice = recipe.Price;
				}
				if (recipe.Price == bestPrice)
					cheapest.Add(recipeKeys[i]);
			}

			if (cheapest.Count == 0)
				return ItemBuilder.CreateRandomPotion();

			return ItemBuilder.CreatePotion(cheapest[UnityEngine.Random.Range(0, cheapest.Count)]);
		}

		DFCareer.Skills RunMartialSkill(PlayerEntity player)
		{
			if (data != null && data.SelectedMartialSkill >= 0)
				return (DFCareer.Skills)data.SelectedMartialSkill;

			DFCareer.Skills skill = DFCareer.Skills.ShortBlade;
			RunWithOSPSeed(0x3000, delegate
			{
				skill = HighestMartialSkill(player);
			});
			if (data != null)
				data.SelectedMartialSkill = (int)skill;

			return skill;
		}

		void GrantStartingSpell(PlayerEntity player)
		{
			AddTemporarySpell(LowestSpellIndexForSkill(HighestMagicSkill(player)), StartingSpellTag);
		}

		void AddIssued(PlayerEntity player, DaggerfallUnityItem item, List<ulong> issued)
		{
			if (item == null)
				return;

			player.Items.AddItem(item, ItemCollection.AddPosition.Back, true);
			issued.Add(item.UID);
		}

		void AddAndEquipIssued(PlayerEntity player, DaggerfallUnityItem item, List<ulong> issued)
		{
			AddIssued(player, item, issued);
			List<DaggerfallUnityItem> unequipped = player.ItemEquipTable.EquipItem(item, true, false);
			if (unequipped == null)
				return;

			for (int i = 0; i < unequipped.Count; i++)
				player.UpdateEquippedArmorValues(unequipped[i], false);
			player.UpdateEquippedArmorValues(item, true);
		}

		DFCareer.Skills HighestMartialSkill(PlayerEntity player)
		{
			List<DFCareer.Skills> best = new List<DFCareer.Skills>();
			int bestValue = -1;
			for (int i = 0; i < MartialSkills.Length; i++)
			{
				int value = player.Skills.GetLiveSkillValue(MartialSkills[i]);
				if (value > bestValue)
				{
					best.Clear();
					best.Add(MartialSkills[i]);
					bestValue = value;
				}
				else if (value == bestValue)
				{
					best.Add(MartialSkills[i]);
				}
			}

			return best[UnityEngine.Random.Range(0, best.Count)];
		}

		DFCareer.Skills HighestMagicSkill(PlayerEntity player)
		{
			List<DFCareer.Skills> best = new List<DFCareer.Skills>();
			int bestValue = -1;
			for (int i = 0; i < MagicSkills.Length; i++)
				ConsiderMagicSkill(player, MagicSkills[i], best, ref bestValue);

			return best[UnityEngine.Random.Range(0, best.Count)];
		}

		void ConsiderMagicSkill(PlayerEntity player, DFCareer.Skills skill, List<DFCareer.Skills> best, ref int bestValue)
		{
			int value = player.Skills.GetLiveSkillValue(skill);
			if (value > bestValue)
			{
				best.Clear();
				best.Add(skill);
				bestValue = value;
			}
			else if (value == bestValue)
			{
				best.Add(skill);
			}
		}

		void FinishRun()
		{
			initialLootPlacementPending = false;
			initialLootPlacementFrames = 0;
			PlayerEntity player = GameManager.Instance.PlayerEntity;
			EffectBundleSettings[] learnedSpellbook = keepLearnedSpellsOnExit ? player.SerializeSpellbook() : null;
			DaggerfallUI.AddHUDText(ExitingText);
			RemoveIssuedItems(player);
			RemoveIssuedItemsFromLooseCollections(player);
			RemoveLessons(player.Items);
			RemoveLessons(player.WagonItems);
			RemoveLessons(player.OtherItems);
			RemoveLessonsFromWorld();
			RemoveLessonsFromLooseCollections();
			RestoreStashedItems(player);
			player.DeserializeSpellbook(data.OriginalSpellbook);
			if (keepLearnedSpellsOnExit)
				RestoreKeptLearnedSpells(player, learnedSpellbook);
			data = new OSPData();
		}

		void RestoreKeptLearnedSpells(PlayerEntity player, EffectBundleSettings[] learnedSpellbook)
		{
			if (learnedSpellbook == null || learnedSpellbook.Length == 0)
				return;

			List<string> knownNames = new List<string>();
			AddSpellNames(knownNames, data.OriginalSpellbook);
			for (int i = 0; i < learnedSpellbook.Length; i++)
			{
				EffectBundleSettings spell = learnedSpellbook[i];
				if (spell.Tag == StartingSpellTag || string.IsNullOrEmpty(spell.Name) || ContainsSpellName(knownNames, spell.Name))
					continue;

				player.AddSpell(spell);
				knownNames.Add(spell.Name);
			}
		}

		void AddSpellNames(List<string> names, EffectBundleSettings[] spells)
		{
			if (names == null || spells == null)
				return;

			for (int i = 0; i < spells.Length; i++)
			{
				if (!string.IsNullOrEmpty(spells[i].Name) && !ContainsSpellName(names, spells[i].Name))
					names.Add(spells[i].Name);
			}
		}

		bool ContainsSpellName(List<string> names, string name)
		{
			if (names == null || string.IsNullOrEmpty(name))
				return false;

			for (int i = 0; i < names.Count; i++)
			{
				if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		void RestoreStashedItems(PlayerEntity player)
		{
			if (data.StashedItemUids != null)
			{
				for (int i = 0; i < data.StashedItemUids.Length; i++)
				{
					DaggerfallUnityItem item = player.OtherItems.GetItem(data.StashedItemUids[i]);
					if (item != null)
						player.Items.Transfer(item, player.OtherItems);
				}
			}

			if (data.StashedGold > 0)
			{
				player.GoldPieces += data.StashedGold;
				data.StashedGold = 0;
			}

			RestoreEquipTable(player);
			player.LightSource = data.OriginalLightSourceUid != 0 ? player.Items.GetItem(data.OriginalLightSourceUid) : null;
		}

		void RestoreEquipTable(PlayerEntity player)
		{
			if (data.OriginalEquipTable == null)
				return;

			UnequipAll(player);
			player.ItemEquipTable.DeserializeEquipTable(data.OriginalEquipTable, player.Items);

			DaggerfallUnityItem[] equipTable = player.ItemEquipTable.EquipTable;
			for (int i = 0; i < equipTable.Length; i++)
			{
				if (equipTable[i] == null)
					continue;

				player.UpdateEquippedArmorValues(equipTable[i], true);
				if (StartEquippedItemMethod != null)
					StartEquippedItemMethod.Invoke(player.ItemEquipTable, new object[] { equipTable[i] });
			}
		}

		void RemoveIssuedItems(PlayerEntity player)
		{
			if (data.IssuedItemUids == null)
				return;

			for (int i = 0; i < data.IssuedItemUids.Length; i++)
			{
				RemoveItem(player.Items, data.IssuedItemUids[i], player);
				RemoveItem(player.WagonItems, data.IssuedItemUids[i], player);
				RemoveItem(player.OtherItems, data.IssuedItemUids[i], player);
			}

			DaggerfallLoot[] loots = FindObjectsOfType<DaggerfallLoot>();
			for (int i = 0; i < loots.Length; i++)
			{
				for (int j = 0; j < data.IssuedItemUids.Length; j++)
					RemoveItem(loots[i].Items, data.IssuedItemUids[j], player);
			}
		}

		void RemoveIssuedItemsFromLooseCollections(PlayerEntity player)
		{
			if (data.IssuedItemUids == null || data.IssuedItemUids.Length == 0)
				return;

			MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
			for (int i = 0; i < behaviours.Length; i++)
			{
				if (behaviours[i] == null)
					continue;

				FieldInfo[] fields = behaviours[i].GetType().GetFields(ItemCollectionFieldFlags);
				for (int j = 0; j < fields.Length; j++)
				{
					ItemCollection collection = fields[j].GetValue(behaviours[i]) as ItemCollection;
					if (collection == null)
						continue;

					for (int k = 0; k < data.IssuedItemUids.Length; k++)
						RemoveItem(collection, data.IssuedItemUids[k], player);
				}
			}
		}

		void RemoveItem(ItemCollection collection, ulong uid, PlayerEntity player)
		{
			if (collection == null)
				return;

			DaggerfallUnityItem item = collection.GetItem(uid);
			if (item == null)
				return;

			if (player.ItemEquipTable.UnequipItem(item))
				player.UpdateEquippedArmorValues(item, false);
			collection.RemoveItem(item);
		}

		void RemoveLessons(ItemCollection collection)
		{
			if (collection == null)
				return;

			for (int i = collection.Count - 1; i >= 0; i--)
			{
				DaggerfallUnityItem item = collection.GetItem(i);
				if (IsLesson(item))
					collection.RemoveItem(item);
			}
		}

		void RemoveLessonsFromWorld()
		{
			DaggerfallLoot[] loots = FindObjectsOfType<DaggerfallLoot>();
			for (int i = 0; i < loots.Length; i++)
				RemoveLessons(loots[i].Items);
		}

		void RemoveLessonsFromLooseCollections()
		{
			MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
			for (int i = 0; i < behaviours.Length; i++)
			{
				if (behaviours[i] == null)
					continue;

				FieldInfo[] fields = behaviours[i].GetType().GetFields(ItemCollectionFieldFlags);
				for (int j = 0; j < fields.Length; j++)
					RemoveLessons(fields[j].GetValue(behaviours[i]) as ItemCollection);
			}
		}

		bool UseLesson(DaggerfallUnityItem item, ItemCollection collection)
		{
			if (!IsLesson(item))
				return false;

			if (data == null || !data.Active)
			{
				DaggerfallUI.SetMidScreenText("The field lesson has faded.");
				if (collection != null)
					collection.RemoveItem(item);
				return true;
			}

			if (AddTemporarySpell(item.value))
			{
				DaggerfallUI.AddHUDText(keepLearnedSpellsOnExit ? "You memorize a spell." : "You memorize a temporary spell.");
				if (collection != null)
					collection.RemoveItem(item);
			}
			else
			{
				DaggerfallUI.SetMidScreenText("The field lesson is illegible.");
			}

			return true;
		}

		bool AddTemporarySpell(int spellIndex)
		{
			return AddTemporarySpell(spellIndex, TempSpellTag);
		}

		bool AddTemporarySpell(int spellIndex, string tag)
		{
			SpellRecord.SpellRecordData spell;
			EffectBundleSettings bundle;
			if (!GameManager.Instance.EntityEffectBroker.GetClassicSpellRecord(spellIndex, out spell))
				return false;
			if (!GameManager.Instance.EntityEffectBroker.ClassicSpellRecordDataToEffectBundleSettings(spell, BundleTypes.Spell, out bundle))
				return false;

			bundle.Tag = tag;
			GameManager.Instance.PlayerEntity.AddSpell(bundle);
			return true;
		}

		DaggerfallUnityItem CreateLesson()
		{
			DaggerfallUnityItem lesson = ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Parchment);
			lesson.value = RandomLeveledSpellIndex();
			lesson.shortName = LessonNamePrefix + SpellName(lesson.value);
			lesson.message = LessonMarker;
			return lesson;
		}

		string SpellName(int spellIndex)
		{
			SpellRecord.SpellRecordData spell;
			if (GameManager.Instance.EntityEffectBroker.GetClassicSpellRecord(spellIndex, out spell) && !string.IsNullOrEmpty(spell.spellName))
				return spell.spellName;

			return "Unknown Spell";
		}

		bool ShouldDropLesson()
		{
			PlayerEntity player = GameManager.Instance.PlayerEntity;
			float magicRatio = SkillRatio(player, MagicSkills);
			float martialRatio = SkillRatio(player, MartialSkills);
			float chance = Mathf.Clamp(0.10f + magicRatio * 0.70f + Mathf.Max(0f, magicRatio - martialRatio) * 0.20f, 0.05f, 0.95f);
			return lessonRandom.NextDouble() < chance;
		}

		float SkillRatio(PlayerEntity player, DFCareer.Skills[] skills)
		{
			if (player == null || skills == null || skills.Length == 0)
				return 0f;

			int sum = 0;
			for (int i = 0; i < skills.Length; i++)
				sum += Clamp(player.Skills.GetLiveSkillValue(skills[i]), 0, 100);

			return sum / (float)(skills.Length * 100);
		}

		int RandomSpellIndex()
		{
			List<SpellRecord.SpellRecordData> spells = new List<SpellRecord.SpellRecordData>();
			foreach (SpellRecord.SpellRecordData spell in GameManager.Instance.EntityEffectBroker.StandardSpells)
			{
				if (spell.effects != null && spell.effects.Length > 0 && !string.IsNullOrEmpty(spell.spellName) && !spell.spellName.StartsWith("!"))
					spells.Add(spell);
			}

			if (spells.Count == 0)
				return 0;

			return spells[UnityEngine.Random.Range(0, spells.Count)].index;
		}

		int LowestSpellIndexForSkill(DFCareer.Skills skill)
		{
			int bestCost = int.MaxValue;
			List<int> best = new List<int>();
			foreach (SpellRecord.SpellRecordData spell in GameManager.Instance.EntityEffectBroker.StandardSpells)
			{
				if (!IsPublicSpell(spell) || spell.cost <= 0 || !SpellUsesSkill(spell, skill))
					continue;

				if (spell.cost < bestCost)
				{
					bestCost = spell.cost;
					best.Clear();
					best.Add(spell.index);
				}
				else if (spell.cost == bestCost)
				{
					best.Add(spell.index);
				}
			}

			return best.Count > 0 ? best[UnityEngine.Random.Range(0, best.Count)] : RandomSpellIndex();
		}

		int RandomLeveledSpellIndex()
		{
			PlayerEntity player = GameManager.Instance.PlayerEntity;
			int level = player != null ? player.Level : 1;
			int minCost = int.MaxValue;
			int maxCost = int.MinValue;
			int count = 0;
			double sum = 0;
			double sumSquares = 0;
			List<SpellRecord.SpellRecordData> spells = new List<SpellRecord.SpellRecordData>();

			foreach (SpellRecord.SpellRecordData spell in GameManager.Instance.EntityEffectBroker.StandardSpells)
			{
				if (!IsPublicSpell(spell) || spell.cost <= 0)
					continue;

				if (spell.cost < minCost)
					minCost = spell.cost;
				if (spell.cost > maxCost)
					maxCost = spell.cost;

				count++;
				sum += spell.cost;
				sumSquares += (double)spell.cost * spell.cost;
			}

			if (count == 0)
				return RandomSpellIndex();

			double average = sum / count;
			double variance = Math.Max(0, (sumSquares / count) - (average * average));
			double standardDeviation = Math.Sqrt(variance);
			double levelRatio = (Clamp(level, LessonMinLevel, LessonMaxLevel) - LessonMinLevel) / (double)(LessonMaxLevel - LessonMinLevel);
			int targetCost = (int)Math.Round(minCost + ((maxCost - minCost) * levelRatio));
			int bandMin = minCost;
			int bandMax = Clamp((int)Math.Round(targetCost + standardDeviation), minCost, maxCost);

			foreach (SpellRecord.SpellRecordData spell in GameManager.Instance.EntityEffectBroker.StandardSpells)
			{
				if (IsPublicSpell(spell) && spell.cost >= bandMin && spell.cost <= bandMax)
					spells.Add(spell);
			}

			if (spells.Count > 0)
				return spells[LessonRandomRange(spells.Count)].index;

			return ClosestSpellIndexToCost(targetCost);
		}

		int ClosestSpellIndexToCost(int targetCost)
		{
			List<SpellRecord.SpellRecordData> spells = new List<SpellRecord.SpellRecordData>();
			int bestDistance = int.MaxValue;
			foreach (SpellRecord.SpellRecordData spell in GameManager.Instance.EntityEffectBroker.StandardSpells)
			{
				if (!IsPublicSpell(spell) || spell.cost <= 0)
					continue;

				int distance = Math.Abs(spell.cost - targetCost);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					spells.Clear();
					spells.Add(spell);
				}
				else if (distance == bestDistance)
				{
					spells.Add(spell);
				}
			}

			if (spells.Count == 0)
				return RandomSpellIndex();

			return spells[LessonRandomRange(spells.Count)].index;
		}

		int Clamp(int value, int min, int max)
		{
			if (value < min)
				return min;
			if (value > max)
				return max;
			return value;
		}

		bool IsPublicSpell(SpellRecord.SpellRecordData spell)
		{
			return spell.effects != null &&
				spell.effects.Length > 0 &&
				!string.IsNullOrEmpty(spell.spellName) &&
				!spell.spellName.StartsWith("!");
		}

		bool SpellUsesSkill(SpellRecord.SpellRecordData spell, DFCareer.Skills skill)
		{
			for (int i = 0; i < spell.effects.Length; i++)
			{
				if (EffectTypeSkill(spell.effects[i].type) == skill)
					return true;
			}

			return false;
		}

		DFCareer.Skills EffectTypeSkill(int effectType)
		{
			switch (effectType)
			{
				case 0:
				case 8:
				case 25:
				case 27:
				case 28:
				case 30:
				case 32:
				case 35:
				case 38:
				case 45:
				case 46:
					return DFCareer.Skills.Alteration;
				case 3:
				case 9:
				case 10:
				case 18:
				case 20:
				case 26:
					return DFCareer.Skills.Restoration;
				case 1:
				case 4:
				case 5:
				case 7:
				case 11:
					return DFCareer.Skills.Destruction;
				case 2:
				case 6:
				case 12:
				case 16:
				case 17:
				case 19:
				case 36:
				case 37:
				case 43:
				case 44:
					return DFCareer.Skills.Mysticism;
				case 14:
				case 21:
				case 22:
				case 31:
				case 33:
				case 34:
				case 39:
				case 40:
				case 41:
				case 47:
				case 48:
				case 49:
				case 50:
					return DFCareer.Skills.Thaumaturgy;
				case 13:
				case 15:
				case 23:
				case 24:
				case 29:
				case 42:
					return DFCareer.Skills.Illusion;
				default:
					return DFCareer.Skills.None;
			}
		}

		int LessonRandomRange(int max)
		{
			return max > 0 ? lessonRandom.Next(max) : 0;
		}

		void RunWithOSPSeed(int salt, Action action)
		{
			if (action == null)
				return;
			if (pureChaosMode)
			{
				action();
				return;
			}

			UnityEngine.Random.State state = UnityEngine.Random.state;
			UnityEngine.Random.InitState(CombineSeed(CurrentDungeonSeed(), salt));
			try
			{
				action();
			}
			finally
			{
				UnityEngine.Random.state = state;
			}
		}

		int CurrentDungeonSeed()
		{
			if (data != null && data.RandomSeed != 0)
				return data.RandomSeed;

			int seed = BuildDungeonSeed(null);
			if (data != null)
				data.RandomSeed = seed;
			return seed;
		}

		int BuildDungeonSeed(DaggerfallDungeon dungeon)
		{
			int seed = 17;
			AddSeed(ref seed, "OSP");

			if (dungeon == null)
			{
				PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
				if (playerEnterExit != null)
					dungeon = playerEnterExit.Dungeon;
			}

			if (dungeon != null)
			{
				AddSeed(ref seed, dungeon.Summary.ID);
				AddSeed(ref seed, dungeon.Summary.RegionName);
				AddSeed(ref seed, dungeon.Summary.LocationName);
				AddSeed(ref seed, (int)dungeon.Summary.LocationType);
				AddSeed(ref seed, (int)dungeon.Summary.DungeonType);
				AddLocationSeed(ref seed, dungeon.Summary.LocationData);
				return NormalizeSeed(seed);
			}

			PlayerGPS gps = GameManager.Instance.PlayerGPS;
			if (gps != null)
			{
				AddSeed(ref seed, gps.CurrentRegionIndex);
				AddSeed(ref seed, gps.CurrentLocationIndex);
				AddSeed(ref seed, gps.CurrentMapID);
				DFPosition pixel = gps.CurrentMapPixel;
				if (pixel != null)
				{
					AddSeed(ref seed, pixel.X);
					AddSeed(ref seed, pixel.Y);
				}
				if (gps.HasCurrentLocation)
					AddLocationSeed(ref seed, gps.CurrentLocation);
			}

			return NormalizeSeed(seed);
		}

		void AddLocationSeed(ref int seed, DFLocation location)
		{
			if (!location.Loaded)
				return;

			AddSeed(ref seed, location.RegionIndex);
			AddSeed(ref seed, location.LocationIndex);
			AddSeed(ref seed, location.MapTableData.MapId);
			AddSeed(ref seed, location.RegionName);
			AddSeed(ref seed, location.Name);
			AddSeed(ref seed, location.HasDungeon ? 1 : 0);
		}

		void AddSeed(ref int seed, int value)
		{
			seed = seed * 397 ^ value;
		}

		void AddSeed(ref int seed, string value)
		{
			if (string.IsNullOrEmpty(value))
				return;

			for (int i = 0; i < value.Length; i++)
				seed = seed * 397 ^ value[i];
		}

		int CombineSeed(int seed, int salt)
		{
			return NormalizeSeed(seed * 397 ^ salt);
		}

		int NormalizeSeed(int seed)
		{
			return seed != 0 ? seed : 1;
		}

		bool IsLesson(DaggerfallUnityItem item)
		{
			return item != null && item.IsParchment && item.message == LessonMarker;
		}

		bool IsSpellbook(DaggerfallUnityItem item)
		{
			return item != null && item.IsOfTemplate(ItemGroups.MiscItems, (int)MiscItems.Spellbook);
		}

		bool SpellbookInWorldLootPool()
		{
			return !grantTemporarySpellbookOnEntry || trueOSPMode;
		}

		bool NeedsWorldSpellbook()
		{
			return SpellbookInWorldLootPool() && !data.SpellbookInjected;
		}

		void QueueInitialLootPlacement()
		{
			initialLootPlacementPending = true;
			initialLootPlacementFrames = InitialLootPlacementDelayFrames;
		}

		void UpdateInitialLootPlacement()
		{
			if (!initialLootPlacementPending)
				return;
			if (initialLootPlacementFrames > 0)
			{
				initialLootPlacementFrames--;
				return;
			}

			initialLootPlacementPending = false;
			if (!NeedsWorldSpellbook() && data.LessonInjected && !NeedsKillPathCheck())
				return;

			PlaceInitialLoot();
		}

		void PlaceInitialLoot()
		{
			List<DaggerfallLoot> loots = GetEligibleLootContainers();
			if (loots.Count == 0)
				loots.Add(CreateFallbackLootContainer());

			EnsureKillPathWeapon(loots);

			if (NeedsWorldSpellbook())
				AddSpellbookToLoot(PickLoot(loots), true);

			if (!data.LessonInjected)
				AddLessonToLoot(PickLoot(loots), true);

			for (int i = 0; i < loots.Count; i++)
				TryPopulateLootContainer(loots[i]);
		}

		List<DaggerfallLoot> GetEligibleLootContainers()
		{
			List<DaggerfallLoot> loots = new List<DaggerfallLoot>();
			foreach (DaggerfallLoot loot in ActiveGameObjectDatabase.GetActiveLoot())
			{
				if (IsEligibleOSPLoot(loot))
					loots.Add(loot);
			}

			return loots;
		}

		bool IsEligibleOSPLoot(DaggerfallLoot loot)
		{
			if (loot == null || loot.Items == null || !loot.gameObject.activeInHierarchy)
				return false;

			return loot.ContainerType == LootContainerTypes.RandomTreasure ||
				loot.ContainerType == LootContainerTypes.CorpseMarker ||
				loot.ContainerType == LootContainerTypes.DroppedLoot;
		}

		DaggerfallLoot PickLoot(List<DaggerfallLoot> loots)
		{
			return loots != null && loots.Count > 0 ? loots[lessonRandom.Next(loots.Count)] : null;
		}

		void TryPopulateLootContainer(DaggerfallLoot loot)
		{
			if (!IsEligibleOSPLoot(loot) || WasLootChecked(loot.LoadID))
				return;

			MarkLootChecked(loot.LoadID);

			if (NeedsWorldSpellbook())
				AddSpellbookToLoot(loot, true);
			else if (SpellbookInWorldLootPool() && !ContainsSpellbook(loot.Items) && lessonRandom.NextDouble() < ExtraSpellbookChance)
				AddSpellbookToLoot(loot, false);

			if (!data.LessonInjected)
				AddLessonToLoot(loot, true);
			else if (!ContainsLesson(loot.Items) && ShouldDropLesson())
				AddLessonToLoot(loot, true);
		}

		bool NeedsKillPathCheck()
		{
			return guaranteedPathToKillAllEnemies && data != null && !data.KillPathWeaponSatisfied;
		}

		void EnsureKillPathWeapon(List<DaggerfallLoot> loots)
		{
			if (!NeedsKillPathCheck())
				return;

			WeaponMaterialTypes required = RequiredDungeonMaterial();
			if (dungeonSpoilerLog)
				LogKillPathInputs(required, loots);
			if (required <= WeaponMaterialTypes.Iron)
			{
				LogKillPath("No gated enemies found.");
				data.KillPathWeaponSatisfied = true;
				return;
			}

			WeaponMaterialTypes material = MinimumMaterialNeededForKillPath(required, loots);
			if (material <= WeaponMaterialTypes.Iron)
			{
				LogKillPath("Existing loot/enemy chain reaches " + required + "; no weapon injected.");
				data.KillPathWeaponSatisfied = true;
				return;
			}

			DaggerfallLoot loot = PickLoot(loots);
			if (loot == null)
				loot = CreateFallbackLootContainer();

			DFCareer.Skills skill = RandomWeaponPathSkill();
			AddKillPathWeaponToLoot(loot, skill, material);
			LogKillPath("Injected " + material + " " + skill + " path weapon into " + LootLabel(loot) + (skill == DFCareer.Skills.Archery ? " with arrows." : "."));
			data.KillPathWeaponSatisfied = true;
		}

		DFCareer.Skills RandomWeaponPathSkill()
		{
			return WeaponPathSkills[lessonRandom.Next(WeaponPathSkills.Length)];
		}

		WeaponMaterialTypes RequiredDungeonMaterial()
		{
			WeaponMaterialTypes required = WeaponMaterialTypes.Iron;
			foreach (DaggerfallEntityBehaviour entityBehaviour in ActiveGameObjectDatabase.GetActiveEnemyBehaviours())
			{
				EnemyEntity enemy;
				if (TryGetRelevantEnemy(entityBehaviour, out enemy) && enemy.MobileEnemy.MinMetalToHit > required)
					required = enemy.MobileEnemy.MinMetalToHit;
			}

			foreach (FoeSpawner spawner in ActiveGameObjectDatabase.GetActiveFoeSpawners())
			{
				if (spawner == null || spawner.AlliedToPlayer || spawner.SpawnCount <= 0 || spawner.FoeType == MobileTypes.None)
					continue;

				MobileEnemy enemy;
				if (EnemyBasics.GetEnemy(spawner.FoeType, out enemy) && enemy.MinMetalToHit > required)
					required = enemy.MinMetalToHit;
			}

			return required;
		}

		WeaponMaterialTypes MinimumMaterialNeededForKillPath(WeaponMaterialTypes required, List<DaggerfallLoot> loots)
		{
			if (MaterialPathProvides(required, loots, WeaponMaterialTypes.None, dungeonSpoilerLog))
				return WeaponMaterialTypes.None;

			for (int material = (int)WeaponMaterialTypes.Steel; material <= (int)required; material++)
			{
				WeaponMaterialTypes candidate = (WeaponMaterialTypes)material;
				if (MaterialPathProvides(required, loots, candidate, dungeonSpoilerLog))
					return candidate;
			}

			return required;
		}

		bool MaterialPathProvides(WeaponMaterialTypes required, List<DaggerfallLoot> loots, WeaponMaterialTypes bonusMaterial, bool log)
		{
			WeaponMaterialTypes accessible = WeaponMaterialTypes.Iron;
			accessible = MaxMaterial(accessible, bonusMaterial);
			PlayerEntity player = GameManager.Instance.PlayerEntity;
			if (player != null)
				accessible = MaxMaterial(accessible, MaxWeaponMaterialInCollection(player.Items));

			string candidate = bonusMaterial > WeaponMaterialTypes.Iron ? bonusMaterial.ToString() : "none";
			if (log)
				LogKillPath("Candidate injected material " + candidate + " starts at " + accessible + ".");

			bool changed = true;
			while (changed)
			{
				changed = false;
				WeaponMaterialTypes next = accessible;
				string source = string.Empty;

				if (loots != null)
				{
					for (int i = 0; i < loots.Count; i++)
					{
						if (loots[i] == null)
							continue;

						WeaponMaterialTypes found = MaxWeaponMaterialInCollection(loots[i].Items);
						if (found > next)
						{
							next = found;
							source = LootLabel(loots[i]);
						}
					}
				}

				foreach (DaggerfallEntityBehaviour entityBehaviour in ActiveGameObjectDatabase.GetActiveEnemyBehaviours())
				{
					EnemyEntity enemy;
					if (TryGetRelevantEnemy(entityBehaviour, out enemy) && enemy.MobileEnemy.MinMetalToHit <= accessible)
					{
						WeaponMaterialTypes found = MaxWeaponMaterialInCollection(enemy.Items);
						if (found > next)
						{
							next = found;
							source = EnemyLabel(enemy) + " corpse path";
						}
					}
				}

				if (next > accessible)
				{
					if (log)
						LogKillPath("Candidate " + candidate + " advances " + accessible + " -> " + next + " via " + source + ".");
					accessible = next;
					changed = true;
				}
			}

			if (log)
				LogKillPath("Candidate " + candidate + " " + (accessible >= required ? "reaches" : "stops at") + " " + accessible + " (target " + required + ").");
			return accessible >= required;
		}

		void LogKillPathInputs(WeaponMaterialTypes required, List<DaggerfallLoot> loots)
		{
			LogKillPath("Checking dungeon. Required material: " + required + ". Eligible loot containers: " + (loots != null ? loots.Count : 0) + ".");
			if (loots != null)
			{
				for (int i = 0; i < loots.Count; i++)
				{
					if (loots[i] != null)
						LogKillPath("Loot " + LootLabel(loots[i]) + " pathWeaponMax=" + MaxWeaponMaterialInCollection(loots[i].Items) + ".");
				}
			}

			foreach (DaggerfallEntityBehaviour entityBehaviour in ActiveGameObjectDatabase.GetActiveEnemyBehaviours())
			{
				EnemyEntity enemy;
				if (TryGetRelevantEnemy(entityBehaviour, out enemy))
					LogKillPath("Enemy " + EnemyLabel(enemy) + " requires=" + enemy.MobileEnemy.MinMetalToHit + " carriesPathWeaponMax=" + MaxWeaponMaterialInCollection(enemy.Items) + ".");
			}

			foreach (FoeSpawner spawner in ActiveGameObjectDatabase.GetActiveFoeSpawners())
			{
				if (spawner == null || spawner.AlliedToPlayer || spawner.SpawnCount <= 0 || spawner.FoeType == MobileTypes.None)
					continue;

				MobileEnemy enemy;
				if (EnemyBasics.GetEnemy(spawner.FoeType, out enemy))
					LogKillPath("Spawner " + spawner.FoeType + " x" + spawner.SpawnCount + " requires=" + enemy.MinMetalToHit + ".");
			}
		}

		void LogKillPath(string message)
		{
			if (!dungeonSpoilerLog)
				return;

			Debug.Log("[OSP] Guaranteed Path: " + message);
		}

		string LootLabel(DaggerfallLoot loot)
		{
			if (loot == null)
				return "unknown loot";

			return loot.ContainerType + " LoadID=" + loot.LoadID;
		}

		string EnemyLabel(EnemyEntity enemy)
		{
			if (enemy == null)
				return "unknown enemy";
			if (TextManager.Instance != null)
				return TextManager.Instance.GetLocalizedEnemyName(enemy.MobileEnemy.ID);

			return ((MobileTypes)enemy.MobileEnemy.ID).ToString();
		}

		bool TryGetRelevantEnemy(DaggerfallEntityBehaviour entityBehaviour, out EnemyEntity enemy)
		{
			enemy = null;
			if (entityBehaviour == null ||
				(entityBehaviour.EntityType != EntityTypes.EnemyMonster && entityBehaviour.EntityType != EntityTypes.EnemyClass))
				return false;

			enemy = entityBehaviour.Entity as EnemyEntity;
			return enemy != null && enemy.MobileEnemy.Team != MobileTeams.PlayerAlly;
		}

		WeaponMaterialTypes MaxWeaponMaterialInCollection(ItemCollection collection)
		{
			WeaponMaterialTypes max = WeaponMaterialTypes.None;
			if (collection == null)
				return max;

			for (int i = 0; i < collection.Count; i++)
			{
				DaggerfallUnityItem item = collection.GetItem(i);
				if (IsMaterialPathWeapon(item))
					max = MaxMaterial(max, WeaponMaterial(item));
			}

			return max;
		}

		bool IsMaterialPathWeapon(DaggerfallUnityItem item)
		{
			if (item == null || item.ItemGroup != ItemGroups.Weapons || item.IsOfTemplate(ItemGroups.Weapons, (int)Weapons.Arrow))
				return false;

			DFCareer.Skills skill = item.GetWeaponSkillID();
			return skill != DFCareer.Skills.None && skill != DFCareer.Skills.Archery;
		}

		WeaponMaterialTypes WeaponMaterial(DaggerfallUnityItem item)
		{
			if (item == null || item.NativeMaterialValue < (int)WeaponMaterialTypes.Iron)
				return WeaponMaterialTypes.None;

			if (item.NativeMaterialValue > (int)WeaponMaterialTypes.Daedric)
				return WeaponMaterialTypes.Daedric;

			return (WeaponMaterialTypes)item.NativeMaterialValue;
		}

		WeaponMaterialTypes MaxMaterial(WeaponMaterialTypes a, WeaponMaterialTypes b)
		{
			return a > b ? a : b;
		}

		void AddKillPathWeaponToLoot(DaggerfallLoot loot, DFCareer.Skills skill, WeaponMaterialTypes material)
		{
			if (loot == null || loot.Items == null)
				return;

			loot.Items.AddItem(CreateMinimumWeaponForSkill(skill, material));
			if (skill == DFCareer.Skills.Archery)
			{
				DaggerfallUnityItem arrows = ItemBuilder.CreateWeapon(Weapons.Arrow, WeaponMaterialTypes.None);
				arrows.stackCount = 20;
				loot.Items.AddItem(arrows);
			}
		}

		DaggerfallUnityItem CreateMinimumWeaponForSkill(DFCareer.Skills skill, WeaponMaterialTypes material)
		{
			switch (skill)
			{
				case DFCareer.Skills.LongBlade:
					return ItemBuilder.CreateWeapon(Weapons.Broadsword, material);
				case DFCareer.Skills.Axe:
					return ItemBuilder.CreateWeapon(Weapons.Battle_Axe, material);
				case DFCareer.Skills.BluntWeapon:
					return ItemBuilder.CreateWeapon(Weapons.Mace, material);
				case DFCareer.Skills.Archery:
					return ItemBuilder.CreateWeapon(Weapons.Short_Bow, material);
				default:
					return ItemBuilder.CreateWeapon(Weapons.Dagger, material);
			}
		}

		void AddSpellbookToLoot(DaggerfallLoot loot, bool marksRequiredSpellbook)
		{
			if (loot == null || loot.Items == null)
				return;
			if (marksRequiredSpellbook && ContainsSpellbook(loot.Items))
			{
				data.SpellbookInjected = true;
				return;
			}

			loot.Items.AddItem(ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Spellbook));
			if (marksRequiredSpellbook)
				data.SpellbookInjected = true;
		}

		void AddLessonToLoot(DaggerfallLoot loot, bool marksRequiredLesson)
		{
			if (loot == null || loot.Items == null)
				return;
			if (marksRequiredLesson && ContainsLesson(loot.Items))
			{
				data.LessonInjected = true;
				return;
			}

			loot.Items.AddItem(CreateLesson());
			if (marksRequiredLesson)
				data.LessonInjected = true;
		}

		bool ContainsSpellbook(ItemCollection collection)
		{
			if (collection == null)
				return false;

			for (int i = 0; i < collection.Count; i++)
			{
				if (IsSpellbook(collection.GetItem(i)))
					return true;
			}

			return false;
		}

		bool ContainsLesson(ItemCollection collection)
		{
			if (collection == null)
				return false;

			for (int i = 0; i < collection.Count; i++)
			{
				if (IsLesson(collection.GetItem(i)))
					return true;
			}

			return false;
		}

		bool WasLootChecked(ulong loadId)
		{
			if (loadId == 0 || data == null || data.CheckedLootUids == null)
				return false;

			for (int i = 0; i < data.CheckedLootUids.Length; i++)
			{
				if (data.CheckedLootUids[i] == loadId)
					return true;
			}

			return false;
		}

		void MarkLootChecked(ulong loadId)
		{
			if (loadId == 0 || data == null || WasLootChecked(loadId))
				return;

			if (data.CheckedLootUids == null)
				data.CheckedLootUids = new ulong[0];

			int index = data.CheckedLootUids.Length;
			Array.Resize(ref data.CheckedLootUids, index + 1);
			data.CheckedLootUids[index] = loadId;
		}

		DaggerfallLoot CreateFallbackLootContainer()
		{
			PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
			int iconIndex = UnityEngine.Random.Range(0, DaggerfallLootDataTables.randomTreasureIconIndices.Length);
			int iconRecord = DaggerfallLootDataTables.randomTreasureIconIndices[iconIndex];
			Vector3 position = GameManager.Instance.PlayerMotor.FindGroundPosition();
			Transform parent = playerEnterExit != null && playerEnterExit.Dungeon != null ? playerEnterExit.Dungeon.transform : null;
			GameObject go = Instantiate(DaggerfallUnity.Instance.Option_LootContainerPrefab.gameObject, position, Quaternion.identity) as GameObject;
			go.name = LootContainerTypes.DroppedLoot.ToString();
			if (parent != null)
				go.transform.parent = parent;

			Billboard billboard = go.GetComponent<Billboard>();
			if (billboard != null)
			{
				billboard.SetMaterial(DaggerfallLootDataTables.randomTreasureArchive, iconRecord);
				position.y += billboard.Summary.Size.y / 2f;
				go.transform.position = position;
			}

			DaggerfallLoot loot = go.GetComponent<DaggerfallLoot>();
			loot.LoadID = DaggerfallUnity.NextUID;
			loot.ContainerType = LootContainerTypes.DroppedLoot;
			loot.ContainerImage = InventoryContainerImages.Chest;
			loot.TextureArchive = DaggerfallLootDataTables.randomTreasureArchive;
			loot.TextureRecord = iconRecord;
			loot.customDrop = true;
			loot.playerOwned = true;
			loot.WorldContext = playerEnterExit != null ? playerEnterExit.WorldContext : WorldContext.Dungeon;
			return loot;
		}

		void UnequipAll(PlayerEntity player)
		{
			foreach (EquipSlots slot in Enum.GetValues(typeof(EquipSlots)))
			{
				if (slot == EquipSlots.None)
					continue;

				DaggerfallUnityItem item = player.ItemEquipTable.UnequipItem(slot);
				if (item != null)
					player.UpdateEquippedArmorValues(item, false);
			}
		}

		class RandomRunRequest
		{
			public int Level;
			public int StartCellX;
			public int StartCellY;
			public int PreviousStartCellX;
			public int PreviousStartCellY;
			public bool PreviousStartInDungeon;
			public bool LevelApplied;
			public bool PlayerChosenLevelDistribution;
			public int PlayerChosenBonusPool;
		}

		class RandomDungeonSelection
		{
			public int RaceIndex = RandomOption;
			public int GenderIndex = RandomOption;
			public int ClassIndex = RandomOption;
			public int Level = RandomLevelOption;
			public int LevelDistribution = LevelDistributionRandom;
			public int SkillGrowthMode = SkillGrowthDefaultWeights;
			public int PrimarySkillWeight = 5;
			public int MajorSkillWeight = 3;
			public int MinorSkillWeight = 2;
			public int RandomSkillChance = 10;
			public int RegionIndex = RandomOption;
			public DFCareer CustomCareer;
			public short MerchantsRep;
			public short PeasantsRep;
			public short ScholarsRep;
			public short NobilityRep;
			public short UnderworldRep;
		}

		struct DungeonStartCandidate
		{
			public int RegionIndex;
			public int LocationIndex;

			public DungeonStartCandidate(int regionIndex, int locationIndex)
			{
				RegionIndex = regionIndex;
				LocationIndex = locationIndex;
			}
		}

		class RandomOSPDungeonWindow : DaggerfallPopupWindow
		{
			OnSiteProcurement owner;
			RandomDungeonSelection selection = new RandomDungeonSelection();
			Panel panel;
			Button raceButton;
			Button genderButton;
			Button classButton;
			Button levelButton;
			Button levelDistributionButton;
			Button skillGrowthButton;
			Button regionButton;
			DaggerfallListPickerWindow picker;
			CreateCharCustomClass customClassWindow;
			List<int> regionChoices = new List<int>();

			public RandomOSPDungeonWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous, OnSiteProcurement owner)
				: base(uiManager, previous)
			{
				this.owner = owner;
			}

			protected override void Setup()
			{
				base.Setup();

				panel = new Panel();
				panel.Size = new Vector2(256, 154);
				panel.HorizontalAlignment = HorizontalAlignment.Center;
				panel.VerticalAlignment = VerticalAlignment.Middle;
				panel.BackgroundColor = new Color(0, 0, 0, 0.86f);
				panel.Outline.Enabled = true;
				NativePanel.Components.Add(panel);

				TextLabel title = DaggerfallUI.AddDefaultShadowedTextLabel(new Vector2(77, 8), panel);
				title.Text = "OSP Quickstart";

				raceButton = AddOptionButton(24, RaceButton_OnMouseClick);
				genderButton = AddOptionButton(38, GenderButton_OnMouseClick);
				classButton = AddOptionButton(52, ClassButton_OnMouseClick);
				levelButton = AddOptionButton(66, LevelButton_OnMouseClick);
				levelDistributionButton = AddOptionButton(80, LevelDistributionButton_OnMouseClick);
				skillGrowthButton = AddOptionButton(94, SkillGrowthButton_OnMouseClick);
				regionButton = AddOptionButton(108, RegionButton_OnMouseClick);

				Button startButton = DaggerfallUI.AddTextButton(new Rect(51, 136, 70, 14), "Start", panel);
				startButton.OnMouseClick += StartButton_OnMouseClick;

				Button cancelButton = DaggerfallUI.AddTextButton(new Rect(135, 136, 70, 14), "Cancel", panel);
				cancelButton.OnMouseClick += CancelButton_OnMouseClick;

				UpdateLabels();
			}

			Button AddOptionButton(int y, BaseScreenComponent.OnMouseClickHandler handler)
			{
				Button button = DaggerfallUI.AddTextButton(new Rect(18, y, 220, 12), string.Empty, panel);
				button.Label.TextScale = 0.78f;
				button.OnMouseClick += handler;
				return button;
			}

			void UpdateLabels()
			{
				raceButton.Label.Text = "Race: " + GetRaceName(selection.RaceIndex);
				genderButton.Label.Text = "Gender: " + GetGenderName(selection.GenderIndex);
				classButton.Label.Text = "Class: " + GetClassName(selection.ClassIndex);
				levelButton.Label.Text = "Level: " + GetLevelName(selection.Level);
				levelDistributionButton.Label.Text = "Level-Up Distribution: " + GetLevelDistributionName(selection.LevelDistribution);
				skillGrowthButton.Label.Text = "Skill Growth: " + GetSkillGrowthName(selection.SkillGrowthMode);
				regionButton.Label.Text = "Region: " + GetRegionName(selection.RegionIndex);
			}

			void RaceButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				picker = new DaggerfallListPickerWindow(uiManager, this);
				picker.ListBox.AddItem("Random");
				for (int i = 0; i < PlayableRaceNames.Length; i++)
					picker.ListBox.AddItem(PlayableRaceNames[i]);
				picker.OnItemPicked += RacePicker_OnItemPicked;
				uiManager.PushWindow(picker);
			}

			void GenderButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				picker = new DaggerfallListPickerWindow(uiManager, this);
				picker.ListBox.AddItem("Random");
				picker.ListBox.AddItem("Male");
				picker.ListBox.AddItem("Female");
				picker.OnItemPicked += GenderPicker_OnItemPicked;
				uiManager.PushWindow(picker);
			}

			void ClassButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				picker = new DaggerfallListPickerWindow(uiManager, this);
				picker.ListBox.AddItem("Random");
				for (int i = 0; i < PlayableClassCareers.Length; i++)
					picker.ListBox.AddItem(PlayableClassCareers[i].ToString());
				picker.ListBox.AddItem("Custom");
				picker.OnItemPicked += ClassPicker_OnItemPicked;
				uiManager.PushWindow(picker);
			}

			void LevelButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				picker = new DaggerfallListPickerWindow(uiManager, this);
				picker.ListBox.AddItem("Random");
				for (int i = 1; i <= owner.GetMaxRandomDungeonLevel(); i++)
					picker.ListBox.AddItem(i.ToString());
				picker.OnItemPicked += LevelPicker_OnItemPicked;
				uiManager.PushWindow(picker);
			}

			void LevelDistributionButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				picker = new DaggerfallListPickerWindow(uiManager, this);
				picker.ListBox.AddItem("Random");
				picker.ListBox.AddItem("Player Chosen");
				picker.OnItemPicked += LevelDistributionPicker_OnItemPicked;
				uiManager.PushWindow(picker);
			}

			void SkillGrowthButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				picker = new DaggerfallListPickerWindow(uiManager, this);
				picker.ListBox.AddItem("Default Weights");
				picker.ListBox.AddItem("Custom Weights");
				picker.ListBox.AddItem("Completely Random");
				picker.OnItemPicked += SkillGrowthPicker_OnItemPicked;
				uiManager.PushWindow(picker);
			}

			void RegionButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				regionChoices = owner.GetDungeonRegionIndices();
				picker = new DaggerfallListPickerWindow(uiManager, this);
				picker.ListBox.AddItem("Random");
				for (int i = 0; i < regionChoices.Count; i++)
					picker.ListBox.AddItem(owner.GetRegionDisplayName(regionChoices[i]));
				picker.OnItemPicked += RegionPicker_OnItemPicked;
				uiManager.PushWindow(picker);
			}

			void RacePicker_OnItemPicked(int index, string itemString)
			{
				selection.RaceIndex = index - 1;
				ClosePicker();
				UpdateLabels();
			}

			void GenderPicker_OnItemPicked(int index, string itemString)
			{
				selection.GenderIndex = index - 1;
				ClosePicker();
				UpdateLabels();
			}

			void ClassPicker_OnItemPicked(int index, string itemString)
			{
				if (index == 0)
				{
					selection.ClassIndex = RandomOption;
					selection.CustomCareer = null;
				}
				else if (index == PlayableClassCareers.Length + 1)
				{
					ClosePicker();
					OpenCustomClassWindow();
					return;
				}
				else
				{
					selection.ClassIndex = index - 1;
					selection.CustomCareer = null;
				}

				ClosePicker();
				UpdateLabels();
			}

			void OpenCustomClassWindow()
			{
				customClassWindow = new CreateCharCustomClass(uiManager);
				customClassWindow.OnClose += CustomClassWindow_OnClose;
				uiManager.PushWindow(customClassWindow);
			}

			void CustomClassWindow_OnClose()
			{
				if (customClassWindow != null && !customClassWindow.Cancelled)
				{
					DFCareer career = owner.CloneCareer(customClassWindow.CreatedClass);
					career.Name = customClassWindow.ClassName;
					career.Strength = customClassWindow.Stats.WorkingStats.LiveStrength;
					career.Intelligence = customClassWindow.Stats.WorkingStats.LiveIntelligence;
					career.Willpower = customClassWindow.Stats.WorkingStats.LiveWillpower;
					career.Agility = customClassWindow.Stats.WorkingStats.LiveAgility;
					career.Endurance = customClassWindow.Stats.WorkingStats.LiveEndurance;
					career.Personality = customClassWindow.Stats.WorkingStats.LivePersonality;
					career.Speed = customClassWindow.Stats.WorkingStats.LiveSpeed;
					career.Luck = customClassWindow.Stats.WorkingStats.LiveLuck;

					selection.ClassIndex = CustomClassOption;
					selection.CustomCareer = career;
					selection.MerchantsRep = customClassWindow.MerchantsRep;
					selection.PeasantsRep = customClassWindow.PeasantsRep;
					selection.ScholarsRep = customClassWindow.ScholarsRep;
					selection.NobilityRep = customClassWindow.NobilityRep;
					selection.UnderworldRep = customClassWindow.UnderworldRep;
				}

				if (customClassWindow != null)
					customClassWindow.OnClose -= CustomClassWindow_OnClose;
				customClassWindow = null;
				UpdateLabels();
			}

			void LevelPicker_OnItemPicked(int index, string itemString)
			{
				selection.Level = index;
				ClosePicker();
				UpdateLabels();
			}

			void LevelDistributionPicker_OnItemPicked(int index, string itemString)
			{
				selection.LevelDistribution = index == 1 ? LevelDistributionPlayerChosen : LevelDistributionRandom;
				ClosePicker();
				UpdateLabels();
			}

			void SkillGrowthPicker_OnItemPicked(int index, string itemString)
			{
				ClosePicker();
				selection.SkillGrowthMode = index == 1 ? SkillGrowthCustomWeights : index == 2 ? SkillGrowthCompletelyRandom : SkillGrowthDefaultWeights;
				if (selection.SkillGrowthMode == SkillGrowthCustomWeights)
				{
					OpenWeightPicker("Primary Weight", PrimarySkillWeightPicker_OnItemPicked);
					return;
				}
				UpdateLabels();
			}

			void PrimarySkillWeightPicker_OnItemPicked(int index, string itemString)
			{
				selection.PrimarySkillWeight = index;
				ClosePicker();
				OpenWeightPicker("Major Weight", MajorSkillWeightPicker_OnItemPicked);
			}

			void MajorSkillWeightPicker_OnItemPicked(int index, string itemString)
			{
				selection.MajorSkillWeight = index;
				ClosePicker();
				OpenWeightPicker("Minor Weight", MinorSkillWeightPicker_OnItemPicked);
			}

			void MinorSkillWeightPicker_OnItemPicked(int index, string itemString)
			{
				selection.MinorSkillWeight = index;
				ClosePicker();
				OpenRandomSkillChancePicker();
			}

			void RandomSkillChancePicker_OnItemPicked(int index, string itemString)
			{
				selection.RandomSkillChance = index * 10;
				ClosePicker();
				UpdateLabels();
			}

			void OpenWeightPicker(string label, DaggerfallListPickerWindow.OnItemPickedEventHandler handler)
			{
				picker = new DaggerfallListPickerWindow(uiManager, this);
				for (int i = 0; i <= 10; i++)
					picker.ListBox.AddItem(label + ": " + i);
				picker.OnItemPicked += handler;
				uiManager.PushWindow(picker);
			}

			void OpenRandomSkillChancePicker()
			{
				picker = new DaggerfallListPickerWindow(uiManager, this);
				for (int i = 0; i <= 100; i += 10)
					picker.ListBox.AddItem("Random Skill: " + i + "%");
				picker.OnItemPicked += RandomSkillChancePicker_OnItemPicked;
				uiManager.PushWindow(picker);
			}

			void RegionPicker_OnItemPicked(int index, string itemString)
			{
				if (index == 0 || regionChoices == null || index - 1 >= regionChoices.Count)
					selection.RegionIndex = RandomOption;
				else
					selection.RegionIndex = regionChoices[index - 1];

				ClosePicker();
				UpdateLabels();
			}

			void StartButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				if (owner.StartRandomOSPDungeon(selection))
					CloseWindow();
			}

			void CancelButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
			{
				CloseWindow();
			}

			void ClosePicker()
			{
				if (picker != null)
					picker.CloseWindow();
				picker = null;
			}

			string GetRaceName(int raceIndex)
			{
				if (raceIndex == RandomOption)
					return "Random";
				return PlayableRaceNames[Mathf.Clamp(raceIndex, 0, PlayableRaceNames.Length - 1)];
			}

			string GetGenderName(int genderIndex)
			{
				if (genderIndex == RandomOption)
					return "Random";
				return genderIndex == 0 ? "Male" : "Female";
			}

			string GetClassName(int classIndex)
			{
				if (classIndex == RandomOption)
					return "Random";
				if (classIndex == CustomClassOption)
				{
					if (selection.CustomCareer != null && !string.IsNullOrEmpty(selection.CustomCareer.Name))
						return "Custom: " + selection.CustomCareer.Name;
					return "Custom";
				}
				return PlayableClassCareers[Mathf.Clamp(classIndex, 0, PlayableClassCareers.Length - 1)].ToString();
			}

			string GetLevelName(int level)
			{
				if (level == RandomLevelOption)
					return "Random";
				return level.ToString();
			}

			string GetLevelDistributionName(int levelDistribution)
			{
				return levelDistribution == LevelDistributionPlayerChosen ? "Player Chosen" : "Random";
			}

			string GetSkillGrowthName(int skillGrowthMode)
			{
				if (skillGrowthMode == SkillGrowthCompletelyRandom)
					return "Random";
				if (skillGrowthMode == SkillGrowthCustomWeights)
					return string.Format("Custom {0}/{1}/{2}/{3}%", selection.PrimarySkillWeight, selection.MajorSkillWeight, selection.MinorSkillWeight, selection.RandomSkillChance);
				return "Default Weights";
			}

			string GetRegionName(int regionIndex)
			{
				if (regionIndex == RandomOption)
					return "Random";
				return owner.GetRegionDisplayName(regionIndex);
			}
		}

		[Serializable]
		public class OSPData
		{
			public bool Active;
			public bool SpellbookInjected;
			public bool LessonInjected;
			public bool KillPathWeaponSatisfied;
			public int RandomSeed;
			public int SelectedMartialSkill = -1;
			public int EnemiesDefeated;
			public int EnemiesTotal;
			public int StashedGold;
			public ulong OriginalLightSourceUid;
			public ulong[] IssuedItemUids = new ulong[0];
			public ulong[] StashedItemUids = new ulong[0];
			public ulong[] CheckedLootUids = new ulong[0];
			public ulong[] OriginalEquipTable = new ulong[0];
			public EffectBundleSettings[] OriginalSpellbook = new EffectBundleSettings[0];
		}
	}
}
