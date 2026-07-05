using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Save;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Serialization;
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

		static Mod mod;
		static OnSiteProcurement instance;
		static readonly MethodInfo StartEquippedItemMethod = typeof(ItemEquipTable).GetMethod("StartEquippedItem", BindingFlags.Instance | BindingFlags.NonPublic);
		static readonly BindingFlags ItemCollectionFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
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
		bool startingDungeonEntryPending;
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
		}

		void OnEnable()
		{
			PlayerEnterExit.OnPreTransition += OnPreTransition;
			PlayerEnterExit.OnFailedTransition += OnFailedTransition;
			PlayerEnterExit.OnTransitionDungeonInterior += OnTransitionDungeonInterior;
			PlayerEnterExit.OnTransitionDungeonExterior += OnTransitionDungeonExterior;
			EnemyDeath.OnEnemyDeath += OnEnemyDeath;
			StartGameBehaviour.OnNewGame += OnNewGame;
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
			SaveLoadManager.OnLoad -= OnLoad;
		}

		void Update()
		{
			if (data == null || !data.Active)
				return;

			GameManager gameManager = GameManager.Instance;
			PlayerEnterExit playerEnterExit = gameManager != null ? gameManager.PlayerEnterExit : null;
			if (playerEnterExit != null && !playerEnterExit.IsPlayerInsideDungeon)
			{
				FinishRun();
				return;
			}

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
		}

		void OnNewGame()
		{
			StartGameBehaviour startGame = GameManager.Instance.StartGameBehaviour;
			startingDungeonEntryPending = startGame != null &&
				startGame.StartMethod == StartGameBehaviour.StartMethods.NewCharacter &&
				DaggerfallUnity.Settings.StartInDungeon;
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
				if (!applyInStartingDungeon)
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

			PlayerEntity player = GameManager.Instance.PlayerEntity;
			data.Active = true;
			if (args != null && args.DaggerfallDungeon != null)
				data.RandomSeed = BuildDungeonSeed(args.DaggerfallDungeon);
			data.OriginalSpellbook = player.SerializeSpellbook();
			player.DeserializeSpellbook(null);
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

		[Serializable]
		public class OSPData
		{
			public bool Active;
			public bool SpellbookInjected;
			public bool LessonInjected;
			public bool KillPathWeaponSatisfied;
			public int RandomSeed;
			public int SelectedMartialSkill = -1;
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
