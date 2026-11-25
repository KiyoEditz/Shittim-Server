using Shittim.Services.Client;
using Schale.Data;
using Schale.Data.GameModel;
using Schale.FlatData;
using Schale.Excel;

namespace Shittim.Commands
{
    [CommandHandler("max", "Max out character stats", "!max <all|charactername>")]
    internal class MaxCommand : Command
    {
        public MaxCommand(IClientConnection connection, string[] args, bool validate = true) : base(connection, args, validate) { }

        [Argument(0, @"^.+$", "Character name or 'all'")]
        public string CharacterName { get; set; } = "all";

        public override async Task Execute()
        {
            using var context = await connection.Context.CreateDbContextAsync();
            var characterExcel = connection.ExcelTableService.GetTable<CharacterExcelT>();
            var characterLevelExcel = connection.ExcelTableService.GetTable<CharacterLevelExcelT>();
            var favorLevelExcel = connection.ExcelTableService.GetTable<FavorLevelExcelT>();
            var potentialExcel = connection.ExcelTableService.GetTable<CharacterPotentialExcelT>();
            var potentialStatExcel = connection.ExcelTableService.GetTable<CharacterPotentialStatExcelT>();
            var weaponExcel = connection.ExcelTableService.GetTable<CharacterWeaponExcelT>();
            var weaponLevelExcel = connection.ExcelTableService.GetTable<CharacterWeaponLevelExcelT>();
            var equipmentExcel = connection.ExcelTableService.GetTable<EquipmentExcelT>();
            var gearExcel = connection.ExcelTableService.GetTable<CharacterGearExcelT>();

            var limits = new MaxLimits(
                MaxCharacterLevel: characterLevelExcel.Count > 0 ? characterLevelExcel.Max(x => x.Level) : 90,
                MaxFavorRank: favorLevelExcel.Count > 0 ? (int)favorLevelExcel.Max(x => x.Level) : 100,
                MaxWeaponLevelCap: weaponLevelExcel.Count > 0 ? weaponLevelExcel.Max(x => x.Level) : 60,
                PotentialsByCharId: potentialExcel
                    .GroupBy(x => x.Id)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                MaxPotentialByGroupId: potentialStatExcel
                    .GroupBy(x => x.PotentialStatGroupId)
                    .ToDictionary(g => g.Key, g => g.Max(s => s.PotentialLevel)),
                GearsByCharId: gearExcel
                    .GroupBy(x => x.CharacterId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Tier).First())
            );

            if (CharacterName.ToLower() == "all")
            {
                await MaxAllCharacters(context, characterExcel, weaponExcel, equipmentExcel, limits);
            }
            else
            {
                await MaxSingleCharacter(context, characterExcel, weaponExcel, equipmentExcel, limits, CharacterName);
            }
        }

        private sealed record MaxLimits(
            int MaxCharacterLevel,
            int MaxFavorRank,
            int MaxWeaponLevelCap,
            Dictionary<long, List<CharacterPotentialExcelT>> PotentialsByCharId,
            Dictionary<long, int> MaxPotentialByGroupId,
            Dictionary<long, CharacterGearExcelT> GearsByCharId);

        private async Task MaxAllCharacters(
            SchaleDataContext context,
            List<CharacterExcelT> characterExcel,
            List<CharacterWeaponExcelT> weaponExcel,
            List<EquipmentExcelT> equipmentExcel,
            MaxLimits limits)
        {
            var ownedCharacters = context.Characters
                .Where(x => x.AccountServerId == connection.AccountServerId)
                .ToList();

            if (ownedCharacters.Count == 0)
            {
                await connection.SendChatMessage("You don't own any characters!");
                return;
            }

            var weaponsByCharId = context.Weapons
                .Where(x => x.AccountServerId == connection.AccountServerId)
                .ToDictionary(x => x.BoundCharacterServerId);
            var equipmentsByServerId = context.Equipments
                .Where(x => x.AccountServerId == connection.AccountServerId && x.BoundCharacterServerId != 0)
                .ToDictionary(x => x.ServerId);
            var gearsByCharId = context.Gears
                .Where(x => x.AccountServerId == connection.AccountServerId)
                .ToDictionary(x => x.BoundCharacterServerId);

            int count = 0;
            int weaponCount = 0;
            int equipCount = 0;
            var pendingEquipAssignments = new List<(CharacterDBServer Character, int SlotIndex, EquipmentDBServer Equip)>();

            foreach (var character in ownedCharacters)
            {
                var characterData = characterExcel.FirstOrDefault(x => x.Id == character.UniqueId);
                if (characterData == null) continue;

                var (wCount, eCount) = ApplyMaxToCharacter(
                    context, character, characterData, weaponExcel, equipmentExcel, limits,
                    weaponsByCharId, equipmentsByServerId, gearsByCharId, pendingEquipAssignments);
                count++;
                weaponCount += wCount;
                equipCount += eCount;
            }

            await context.SaveChangesAsync();

            if (pendingEquipAssignments.Count > 0)
            {
                foreach (var (character, slotIndex, equip) in pendingEquipAssignments)
                {
                    character.EquipmentServerIds![slotIndex] = equip.ServerId;
                    context.Characters.Update(character);
                }
                await context.SaveChangesAsync();
            }

            await connection.SendChatMessage($"Maxed out {count} characters, {weaponCount} weapons, and {equipCount} equipment!");
        }

        private async Task MaxSingleCharacter(
            SchaleDataContext context,
            List<CharacterExcelT> characterExcel,
            List<CharacterWeaponExcelT> weaponExcel,
            List<EquipmentExcelT> equipmentExcel,
            MaxLimits limits,
            string name)
        {
            var characterData = characterExcel.FirstOrDefault(x =>
                x.DevName != null &&
                x.DevName.Contains(name, StringComparison.OrdinalIgnoreCase));

            if (characterData == null)
            {
                await connection.SendChatMessage($"Character '{name}' not found!");
                return;
            }

            var character = context.Characters.FirstOrDefault(x =>
                x.AccountServerId == connection.AccountServerId &&
                x.UniqueId == characterData.Id);

            if (character == null)
            {
                await connection.SendChatMessage($"You don't own {characterData.DevName}!");
                return;
            }

            var weaponsByCharId = context.Weapons
                .Where(x => x.AccountServerId == connection.AccountServerId && x.BoundCharacterServerId == character.ServerId)
                .ToDictionary(x => x.BoundCharacterServerId);
            var equipmentsByServerId = context.Equipments
                .Where(x => x.AccountServerId == connection.AccountServerId && x.BoundCharacterServerId == character.ServerId)
                .ToDictionary(x => x.ServerId);
            var gearsByCharId = context.Gears
                .Where(x => x.AccountServerId == connection.AccountServerId && x.BoundCharacterServerId == character.ServerId)
                .ToDictionary(x => x.BoundCharacterServerId);
            var pendingEquipAssignments = new List<(CharacterDBServer Character, int SlotIndex, EquipmentDBServer Equip)>();

            var (weaponCount, equipCount) = ApplyMaxToCharacter(
                context, character, characterData, weaponExcel, equipmentExcel, limits,
                weaponsByCharId, equipmentsByServerId, gearsByCharId, pendingEquipAssignments);

            await context.SaveChangesAsync();

            if (pendingEquipAssignments.Count > 0)
            {
                foreach (var (ch, slotIndex, equip) in pendingEquipAssignments)
                {
                    ch.EquipmentServerIds![slotIndex] = equip.ServerId;
                    context.Characters.Update(ch);
                }
                await context.SaveChangesAsync();
            }

            await connection.SendChatMessage($"Maxed out {characterData.DevName}! (Lv{character.Level}, {character.StarGrade}★, Favor {character.FavorRank}, Skills 10/5/10/10, PotentialStats maxed, {weaponCount} weapon, {equipCount} equipment)");
        }

        private (int WeaponCount, int EquipCount) ApplyMaxToCharacter(
            SchaleDataContext context,
            CharacterDBServer character,
            CharacterExcelT characterData,
            List<CharacterWeaponExcelT> weaponExcel,
            List<EquipmentExcelT> equipmentExcel,
            MaxLimits limits,
            Dictionary<long, WeaponDBServer> weaponsByCharId,
            Dictionary<long, EquipmentDBServer> equipmentsByServerId,
            Dictionary<long, GearDBServer> gearsByCharId,
            List<(CharacterDBServer Character, int SlotIndex, EquipmentDBServer Equip)> pendingEquipAssignments)
        {
            character.Level = limits.MaxCharacterLevel;
            character.Exp = 0;
            character.StarGrade = characterData.MaxStarGrade;
            character.FavorRank = limits.MaxFavorRank;
            character.FavorExp = 0;
            character.PublicSkillLevel = 10;
            character.ExSkillLevel = 5;
            character.PassiveSkillLevel = 10;
            character.ExtraPassiveSkillLevel = 10;

            if (limits.PotentialsByCharId.TryGetValue(character.UniqueId, out var charPotentials))
            {
                var potentialStats = new Dictionary<int, int>();
                foreach (var pot in charPotentials)
                {
                    if (pot.IsUnnecessaryStat) continue;
                    var maxPotLevel = limits.MaxPotentialByGroupId.GetValueOrDefault(pot.PotentialStatGroupId, 25);
                    potentialStats[(int)pot.PotentialStatBonusRateType] = maxPotLevel;
                }
                character.PotentialStats = potentialStats;
            }
            else
            {
                character.PotentialStats = new Dictionary<int, int> { { 1, 25 }, { 2, 25 }, { 3, 25 } };
            }

            int weaponCount = 0;
            var weaponData = weaponExcel.FirstOrDefault(x => x.Id == character.UniqueId);
            if (weaponData != null)
            {
                var maxWeaponStar = weaponData.Unlock != null ? Math.Max(1, weaponData.Unlock.Count(u => u)) : 3;
                var starWeaponLevelCap = weaponData.MaxLevel != null && weaponData.MaxLevel.Count >= maxWeaponStar
                    ? weaponData.MaxLevel[maxWeaponStar - 1]
                    : limits.MaxWeaponLevelCap;
                var maxWeaponLevel = Math.Min(starWeaponLevelCap, limits.MaxWeaponLevelCap);

                if (weaponsByCharId.TryGetValue(character.ServerId, out var existingWeapon))
                {
                    existingWeapon.StarGrade = maxWeaponStar;
                    existingWeapon.Level = maxWeaponLevel;
                    existingWeapon.Exp = 0;
                    context.Weapons.Update(existingWeapon);
                }
                else
                {
                    var newWeapon = new WeaponDBServer
                    {
                        UniqueId = weaponData.Id,
                        BoundCharacterServerId = character.ServerId,
                        StarGrade = maxWeaponStar,
                        Level = maxWeaponLevel,
                        Exp = 0
                    };
                    context.AddWeapons(connection.AccountServerId, [newWeapon]);
                    weaponsByCharId[character.ServerId] = newWeapon;
                }
                weaponCount = 1;
            }

            if (limits.GearsByCharId.TryGetValue(character.UniqueId, out var bestGear))
            {
                if (gearsByCharId.TryGetValue(character.ServerId, out var existingGear))
                {
                    existingGear.UniqueId = bestGear.Id;
                    existingGear.Tier = (int)bestGear.Tier;
                    existingGear.Level = 1;
                    existingGear.Exp = 0;
                    context.Gears.Update(existingGear);
                }
                else
                {
                    var newGear = new GearDBServer
                    {
                        UniqueId = bestGear.Id,
                        Level = 1,
                        SlotIndex = 4,
                        BoundCharacterServerId = character.ServerId,
                        Tier = (int)bestGear.Tier,
                        Exp = 0
                    };
                    context.AddGears(connection.AccountServerId, [newGear]);
                    gearsByCharId[character.ServerId] = newGear;
                }
            }

            int equipCount = 0;
            character.EquipmentServerIds ??= [];
            while (character.EquipmentServerIds.Count < 3)
                character.EquipmentServerIds.Add(0);

            if (characterData.EquipmentSlot != null)
            {
                for (int i = 0; i < Math.Min(3, characterData.EquipmentSlot.Count); i++)
                {
                    var slotCategory = characterData.EquipmentSlot[i];
                    var bestEquip = equipmentExcel
                        .GetEquipmentExcelByCategory(slotCategory)
                        .GetCharacterEquipment()
                        .OrderByDescending(x => x.TierInit)
                        .ThenByDescending(x => x.MaxLevel)
                        .FirstOrDefault();

                    if (bestEquip == null) continue;

                    var currentEquipId = character.EquipmentServerIds[i];
                    if (currentEquipId != 0 && equipmentsByServerId.TryGetValue(currentEquipId, out var existingEquip))
                    {
                        existingEquip.UniqueId = bestEquip.Id;
                        existingEquip.Tier = (int)bestEquip.TierInit;
                        existingEquip.Level = bestEquip.MaxLevel;
                        existingEquip.Exp = 0;
                        existingEquip.StackCount = 1;
                        context.Equipments.Update(existingEquip);
                    }
                    else
                    {
                        var newEquip = new EquipmentDBServer
                        {
                            UniqueId = bestEquip.Id,
                            Tier = (int)bestEquip.TierInit,
                            Level = bestEquip.MaxLevel,
                            Exp = 0,
                            StackCount = 1,
                            BoundCharacterServerId = character.ServerId
                        };
                        context.AddEquipment(connection.AccountServerId, [newEquip]);
                        pendingEquipAssignments.Add((character, i, newEquip));
                    }
                    equipCount++;
                }
            }

            context.Characters.Update(character);
            return (weaponCount, equipCount);
        }
    }
}
