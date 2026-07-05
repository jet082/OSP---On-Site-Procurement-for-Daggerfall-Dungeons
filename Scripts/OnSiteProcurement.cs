using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Save;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using UnityEngine;

namespace OnSiteProcurementMod
{
	public class OnSiteProcurement : MonoBehaviour, IHasModSaveData
	{
		const string LessonName = "OSP Field Lesson";
		const string TempSpellTag = "OSP";
		const string EnteringText = "Entering Dungeon - Depositing Items/Spells";
		const string ExitingText = "Exiting Dungeon - Restoring Items/Spells";
		const int LessonMarker = 0x05F05F;

		static Mod mod;
		static OnSiteProcurement instance;
		static readonly MethodInfo StartEquippedItemMethod = typeof(ItemEquipTable).GetMethod("StartEquippedItem", BindingFlags.Instance | BindingFlags.NonPublic);
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
		bool waitingForDungeonLoot;
		bool ospEnabled = true;
		bool applyInStartingDungeon = true;
		bool grantTemporarySpellbookOnEntry;
		bool startingDungeonEntryPending;

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
			ApplySettings(settings);
			if (wasEnabled && !ospEnabled && data != null && data.Active)
				FinishRun();
		}

		void ApplySettings(ModSettings settings)
		{
			ospEnabled = settings.GetBool("General", "Enabled");
			applyInStartingDungeon = settings.GetBool("General", "ApplyInStartingDungeon");
			grantTemporarySpellbookOnEntry = settings.GetBool("General", "GrantTemporarySpellbookOnEntry");
		}

		void OnEnable()
		{
			PlayerEnterExit.OnPreTransition += OnPreTransition;
			PlayerEnterExit.OnFailedTransition += OnFailedTransition;
			PlayerEnterExit.OnTransitionDungeonInterior += OnTransitionDungeonInterior;
			PlayerEnterExit.OnTransitionDungeonExterior += OnTransitionDungeonExterior;
			LootTables.OnLootSpawned += OnLootSpawned;
			StartGameBehaviour.OnNewGame += OnNewGame;
			SaveLoadManager.OnLoad += OnLoad;
		}

		void OnDisable()
		{
			PlayerEnterExit.OnPreTransition -= OnPreTransition;
			PlayerEnterExit.OnFailedTransition -= OnFailedTransition;
			PlayerEnterExit.OnTransitionDungeonInterior -= OnTransitionDungeonInterior;
			PlayerEnterExit.OnTransitionDungeonExterior -= OnTransitionDungeonExterior;
			LootTables.OnLootSpawned -= OnLootSpawned;
			StartGameBehaviour.OnNewGame -= OnNewGame;
			SaveLoadManager.OnLoad -= OnLoad;
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
			waitingForDungeonLoot = false;
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
			waitingForDungeonLoot = true;
			data = new OSPData();
			BeginStash();
		}

		void OnFailedTransition(PlayerEnterExit.TransitionEventArgs args)
		{
			if (args.TransitionType != PlayerEnterExit.TransitionType.ToDungeonInterior || !entryPending)
				return;

			RestoreStashedItems(GameManager.Instance.PlayerEntity);
			data = new OSPData();
			entryPending = false;
			waitingForDungeonLoot = false;
		}

		void OnTransitionDungeonInterior(PlayerEnterExit.TransitionEventArgs args)
		{
			if (!entryPending)
				return;

			PlayerEntity player = GameManager.Instance.PlayerEntity;
			data.Active = true;
			data.OriginalSpellbook = player.SerializeSpellbook();
			player.DeserializeSpellbook(null);
			DaggerfallUI.AddHUDText(EnteringText);

			GrantFieldKit(player);

			if (!data.SpellbookInjected)
				InjectDungeonFallbackLoot();

			entryPending = false;
			waitingForDungeonLoot = false;
		}

		void OnTransitionDungeonExterior(PlayerEnterExit.TransitionEventArgs args)
		{
			if (data == null || !data.Active)
				return;

			FinishRun();
		}

		void OnLootSpawned(object sender, TabledLootSpawnedEventArgs args)
		{
			if (!waitingForDungeonLoot || args == null || args.Items == null)
				return;

			if (!grantTemporarySpellbookOnEntry && !data.SpellbookInjected)
			{
				args.Items.AddItem(ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Spellbook));
				data.SpellbookInjected = true;
			}

			args.Items.AddItem(CreateLesson());
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

			switch (HighestMartialSkill(player))
			{
				case DFCareer.Skills.HandToHand:
					AddIssued(player, ItemBuilder.CreateRandomPotion(), issued);
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
			DFCareer.Skills[] skills = {
				DFCareer.Skills.ShortBlade,
				DFCareer.Skills.LongBlade,
				DFCareer.Skills.HandToHand,
				DFCareer.Skills.Axe,
				DFCareer.Skills.BluntWeapon,
				DFCareer.Skills.Archery
			};

			List<DFCareer.Skills> best = new List<DFCareer.Skills>();
			int bestValue = -1;
			for (int i = 0; i < skills.Length; i++)
			{
				int value = player.Skills.GetLiveSkillValue(skills[i]);
				if (value > bestValue)
				{
					best.Clear();
					best.Add(skills[i]);
					bestValue = value;
				}
				else if (value == bestValue)
				{
					best.Add(skills[i]);
				}
			}

			return best[UnityEngine.Random.Range(0, best.Count)];
		}

		void FinishRun()
		{
			PlayerEntity player = GameManager.Instance.PlayerEntity;
			DaggerfallUI.AddHUDText(ExitingText);
			RemoveIssuedItems(player);
			RemoveLessons(player.Items);
			RemoveLessons(player.WagonItems);
			RemoveLessonsFromWorld();
			RestoreStashedItems(player);
			player.DeserializeSpellbook(data.OriginalSpellbook);
			data = new OSPData();
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
				DaggerfallUI.AddHUDText("You memorize a temporary spell.");
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
			SpellRecord.SpellRecordData spell;
			EffectBundleSettings bundle;
			if (!GameManager.Instance.EntityEffectBroker.GetClassicSpellRecord(spellIndex, out spell))
				return false;
			if (!GameManager.Instance.EntityEffectBroker.ClassicSpellRecordDataToEffectBundleSettings(spell, BundleTypes.Spell, out bundle))
				return false;

			bundle.Tag = TempSpellTag;
			GameManager.Instance.PlayerEntity.AddSpell(bundle);
			return true;
		}

		DaggerfallUnityItem CreateLesson()
		{
			DaggerfallUnityItem lesson = ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Parchment);
			lesson.shortName = LessonName;
			lesson.message = LessonMarker;
			lesson.value = RandomSpellIndex();
			return lesson;
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

		bool IsLesson(DaggerfallUnityItem item)
		{
			return item != null && item.IsParchment && item.message == LessonMarker;
		}

		void InjectDungeonFallbackLoot()
		{
			DaggerfallLoot loot = CreateFallbackLootContainer();
			loot.Items.AddItem(ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Spellbook));
			loot.Items.AddItem(CreateLesson());
			data.SpellbookInjected = true;
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
			public int StashedGold;
			public ulong OriginalLightSourceUid;
			public ulong[] IssuedItemUids = new ulong[0];
			public ulong[] StashedItemUids = new ulong[0];
			public ulong[] OriginalEquipTable = new ulong[0];
			public EffectBundleSettings[] OriginalSpellbook = new EffectBundleSettings[0];
		}
	}
}
