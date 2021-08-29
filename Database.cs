using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using SimpleStack.Orm;
using SimpleStack.Orm.Attributes;
using SimpleStack.Orm.MySQLConnector;
using UnityEngine;
using UnityEngine.Events;

public enum MySqlSSLMode : byte { None, Preferred, Required, VerifyCA, VerifyFull };

public partial class Database : MonoBehaviour
{

    // singleton for easier access
    public static Database singleton;

    public OrmConnectionFactory connFactory;
    // connection (public so it can be used by addons)
    public OrmConnection connection;

    // Database settings
    public string serverIP = "127.0.0.1"; // localhost for development
    public string port = "3306"; // default mysql port is 3306
    public string userID = "root"; // should never be root in production
    public string password = "password"; // should never be hardcoded in production (even through the editor)
    // keep databaseName case insensitive, some dbs support case others don't so its better to assum no case
    public const string databaseName = "ummorpg"; // doesn't need to exist, schema will be generated
    /* SSL mode options
     * Preferred - (this is the default). Use SSL if the server supports it.
     * None - Do not use SSL.
     * Required - Always use SSL. Deny connection if server does not support SSL. Does not validate CA or hostname.
     * VerifyCA - Always use SSL. Validates the CA but tolerates hostname mismatch.
     * VerifyFull - Always use SSL. Validates CA and hostname.
     */
    public MySqlSSLMode sslMode = MySqlSSLMode.None;
    

    [Header("Events")]
    // use onConnected to create an extra table for your addon
    public UnityEvent onConnected;
    public UnityEventPlayer onCharacterLoad;
    public UnityEventPlayer onCharacterSave;

    void Awake()
    {
        // initialize singleton
        if (singleton == null) singleton = this;
    }

    // connect
    // only call this from the server, not from the client.
    public void Connect()
    {
#if UNITY_EDITOR
        string connectionString = "server=" + serverIP + ";port=" + port +
            ";uid=" + userID + ";pwd=" + password +
            ";sslmode=" + sslMode + ";AllowPublicKeyRetrieval=true" +
            ";AllowUserVariables=true;ApplicationName=" + Application.productName;
#elif UNITY_ANDROID
        Debug.LogError("MySql is not supported on this platform");
#elif UNITY_IOS
        Debug.LogError("MySql is not supported on this platform");
#else
        string connectionString = "server=" + serverIP + ";port=" + port +
            ";uid=" + userID + ";pwd=" + password + ";sslmode=" + sslMode +
            ";AllowUserVariables=true;ApplicationName=" + Application.productName;
#endif 
        // open connection
        connFactory = new OrmConnectionFactory(new MySqlConnectorDialectProvider(), connectionString);
        connection = connFactory.OpenConnection();

        connection.CreateSchemaIfNotExists(databaseName);
        connection.CreateTableIfNotExists<Account>();
        connection.CreateTableIfNotExists<Character>();
        connection.CreateTableIfNotExists<CharacterInventory>();
        connection.CreateTableIfNotExists<CharacterEquipment>();
        connection.CreateTableIfNotExists<CharacterItemCooldown>();
        connection.CreateTableIfNotExists<CharacterSkill>();
        connection.CreateTableIfNotExists<CharacterBuff>();
        connection.CreateTableIfNotExists<CharacterQuest>();
        connection.CreateTableIfNotExists<CharacterOrders>();
        connection.CreateTableIfNotExists<CharacterGuild>();
        connection.CreateTableIfNotExists<GuildInfo>();

        // addon system hooks
        onConnected.Invoke();

        Debug.Log("MySQL Database connection established");
    }

    // close connection when Unity closes to prevent locking
    void OnApplicationQuit()
    {
        connection?.Close();
    }

    // account data ////////////////////////////////////////////////////////////
    // try to log in with an account.
    // -> not called 'CheckAccount' or 'IsValidAccount' because it both checks
    //    if the account is valid AND sets the lastlogin field
    public bool TryLogin(string name, string password)
    {
        // this function can be used to verify account credentials in a database
        // or a content management system.
        //
        // for example, we could setup a content management system with a forum,
        // news, shop etc. and then use a simple HTTP-GET to check the account
        // info, for example:
        //
        //   var request = new WWW("example.com/verify.php?id="+id+"&amp;pw="+pw);
        //   while (!request.isDone)
        //       Debug.Log("loading...");
        //   return request.error == null && request.text == "ok";
        //
        // where verify.php is a script like this one:
        //   <?php
        //   // id and pw set with HTTP-GET?
        //   if (isset($_GET['id']) && isset($_GET['pw'])) {
        //       // validate id and pw by using the CMS, for example in Drupal:
        //       if (user_authenticate($_GET['id'], $_GET['pw']))
        //           echo "ok";
        //       else
        //           echo "invalid id or pw";
        //   }
        //   ?>
        //
        // or we could check in a MYSQL database:
        //   var dbConn = new MySql.Data.MySqlClient.MySqlConnection("Persist Security Info=False;server=localhost;database=notas;uid=root;password=" + dbpwd);
        //   var cmd = dbConn.CreateCommand();
        //   cmd.CommandText = "SELECT id FROM accounts WHERE id='" + account + "' AND pw='" + password + "'";
        //   dbConn.Open();
        //   var reader = cmd.ExecuteReader();
        //   if (reader.Read())
        //       return reader.ToString() == account;
        //   return false;
        //
        // as usual, we will use the simplest solution possible:
        // create account if not exists, compare password otherwise.
        // no CMS communication necessary and good enough for an Indie MMORPG.

        // not empty?
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(password))
        {
            // demo feature: create account if it doesn't exist yet.
            Account account = connection.FirstOrDefault<Account>(q => q.name == name);
            if (account == null)
            {
                connection.Insert<Account>(new Account { name = name, password = password, created = DateTime.UtcNow, lastlogin = DateTime.Now, banned = false });
                return true;
            }

            // check account name, password, banned status
            else if (!account.banned && account.password == password)
            {
                // save last login time and return true
                account.lastlogin = DateTime.UtcNow;
                connection.Update<Account>(account);
                return true;
            }
        }
        return false;
    }

    // character data //////////////////////////////////////////////////////////
    public bool CharacterExists(string characterName)
    {
        // checks deleted ones too so we don't end up with duplicates if we un-
        // delete one
        return connection.FirstOrDefault<Character>(q => q.name == characterName) != null;
    }

    public void CharacterDelete(string characterName)
    {
        // soft delete the character so it can always be restored later
        connection.UpdateAll<Character>(new {deleted = true}, x => x.name == characterName);
    }

    // returns the list of character names for that account
    // => all the other values can be read with CharacterLoad!
    public List<string> CharactersForAccount(string account)
    {
        List<string> result = new List<string>();
        foreach (Character character in connection.Select<Character>(q => q.account == account && q.deleted == false))
        {
            result.Add(character.name);
        }
        return result;
    }

    void LoadInventory(PlayerInventory inventory)
    {
        // fill all slots first
        for (int i = 0; i < inventory.size; ++i)
            inventory.slots.Add(new ItemSlot());

        // then load valid items and put into their slots
        // (one big query is A LOT faster than querying each slot separately)
        foreach (CharacterInventory row in connection.Select<CharacterInventory>(q => q.character == inventory.name))
        {
            if (row.slot < inventory.size)
            {
                if (ScriptableItem.All.TryGetValue(row.name.GetStableHashCode(), out ScriptableItem itemData))
                {
                    Item item = new Item(itemData);
                    item.durability = Mathf.Min(row.durability, item.maxDurability);
                    item.summonedHealth = row.summonedHealth;
                    item.summonedLevel = row.summonedLevel;
                    item.summonedExperience = row.summonedExperience;
                    inventory.slots[row.slot] = new ItemSlot(item, row.amount);
                }
                else Debug.LogWarning("LoadInventory: skipped item " + row.name + " for " + inventory.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
            }
            else Debug.LogWarning("LoadInventory: skipped slot " + row.slot + " for " + inventory.name + " because it's bigger than size " + inventory.size);
        }
    }

    void LoadEquipment(PlayerEquipment equipment)
    {
        // fill all slots first
        for (int i = 0; i < equipment.slotInfo.Length; ++i)
            equipment.slots.Add(new ItemSlot());

        // then load valid equipment and put into their slots
        // (one big query is A LOT faster than querying each slot separately)
        foreach (CharacterEquipment row in connection.Select<CharacterEquipment>(q => q.character == equipment.name))
        {
            if (row.slot < equipment.slotInfo.Length)
            {
                if (ScriptableItem.All.TryGetValue(row.name.GetStableHashCode(), out ScriptableItem itemData))
                {
                    Item item = new Item(itemData);
                    item.durability = Mathf.Min(row.durability, item.maxDurability);
                    item.summonedHealth = row.summonedHealth;
                    item.summonedLevel = row.summonedLevel;
                    item.summonedExperience = row.summonedExperience;
                    equipment.slots[row.slot] = new ItemSlot(item, row.amount);
                }
                else Debug.LogWarning("LoadEquipment: skipped item " + row.name + " for " + equipment.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
            }
            else Debug.LogWarning("LoadEquipment: skipped slot " + row.slot + " for " + equipment.name + " because it's bigger than size " + equipment.slotInfo.Length);
        }
    }

    void LoadItemCooldowns(Player player)
    {
        // then load cooldowns
        // (one big query is A LOT faster than querying each slot separately)
        foreach (CharacterItemCooldown row in connection.Select<CharacterItemCooldown>(q => q.character == player.name))
        {
            // cooldownEnd is based on NetworkTime.time which will be different
            // when restarting a server, hence why we saved it as just the
            // remaining time. so let's convert it back again.
            player.itemCooldowns.Add(row.category, row.cooldownEnd + NetworkTime.time);
        }
    }

    void LoadSkills(PlayerSkills skills)
    {
        // load skills based on skill templates (the others don't matter)
        // -> this way any skill changes in a prefab will be applied
        //    to all existing players every time (unlike item templates
        //    which are only for newly created characters)

        // fill all slots first
        foreach (ScriptableSkill skillData in skills.skillTemplates)
            skills.skills.Add(new Skill(skillData));

        // then load learned skills and put into their slots
        // (one big query is A LOT faster than querying each slot separately)
        foreach (CharacterSkill row in connection.Select<CharacterSkill>(q => q.character == skills.name))
        {
            int index = skills.GetSkillIndexByName(row.name);
            if (index != -1)
            {
                Skill skill = skills.skills[index];
                // make sure that 1 <= level <= maxlevel (in case we removed a skill
                // level etc)
                skill.level = Mathf.Clamp(row.level, 1, skill.maxLevel);
                // make sure that 1 <= level <= maxlevel (in case we removed a skill
                // level etc)
                // castTimeEnd and cooldownEnd are based on NetworkTime.time
                // which will be different when restarting a server, hence why
                // we saved them as just the remaining times. so let's convert
                // them back again.
                skill.castTimeEnd = row.castTimeEnd + NetworkTime.time;
                skill.cooldownEnd = row.cooldownEnd + NetworkTime.time;

                skills.skills[index] = skill;
            }
        }
    }

    void LoadBuffs(PlayerSkills skills)
    {
        // load buffs
        // note: no check if we have learned the skill for that buff
        //       since buffs may come from other people too
        foreach (CharacterBuff row in connection.Select<CharacterBuff>(q => q.character == skills.name))
        {
            if (ScriptableSkill.All.TryGetValue(row.name.GetStableHashCode(), out ScriptableSkill skillData))
            {
                // make sure that 1 <= level <= maxlevel (in case we removed a skill
                // level etc)
                int level = Mathf.Clamp(row.level, 1, skillData.maxLevel);
                Buff buff = new Buff((BuffSkill)skillData, level);
                // buffTimeEnd is based on NetworkTime.time, which will be
                // different when restarting a server, hence why we saved
                // them as just the remaining times. so let's convert them
                // back again.
                buff.buffTimeEnd = row.buffTimeEnd + NetworkTime.time;
                skills.buffs.Add(buff);
            }
            else Debug.LogWarning("LoadBuffs: skipped buff " + row.name + " for " + skills.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
        }
    }

    void LoadQuests(PlayerQuests quests)
    {
        // load quests
        foreach (CharacterQuest row in connection.Select<CharacterQuest>(q => q.character == quests.name))
        {
            ScriptableQuest questData;
            if (ScriptableQuest.All.TryGetValue(row.name.GetStableHashCode(), out questData))
            {
                Quest quest = new Quest(questData);
                quest.progress = row.progress;
                quest.completed = row.completed;
                quests.quests.Add(quest);
            }
            else Debug.LogWarning("LoadQuests: skipped quest " + row.name + " for " + quests.name + " because it doesn't exist anymore. If it wasn't removed intentionally then make sure it's in the Resources folder.");
        }
    }

    // only load guild when their first player logs in
    // => using NetworkManager.Awake to load all guilds.Where would work,
    //    but we would require lots of memory and it might take a long time.
    // => hooking into player loading to load guilds is a really smart solution,
    //    because we don't ever have to load guilds that aren't needed
    void LoadGuildOnDemand(PlayerGuild playerGuild)
    {
        string guildName = connection.GetScalar<CharacterGuild, string>(x => x.guild, y => y.character == playerGuild.name);
        if (guildName != null)
        {
            // load guild on demand when the first player of that guild logs in
            // (= if it's not in GuildSystem.guilds yet)
            if (!GuildSystem.guilds.ContainsKey(guildName))
            {
                Guild guild = LoadGuild(guildName);
                GuildSystem.guilds[guild.name] = guild;
                playerGuild.guild = guild;
            }
            // assign from already loaded guild
            else playerGuild.guild = GuildSystem.guilds[guildName];
        }
    }

    public GameObject CharacterLoad(string characterName, List<Player> prefabs, bool isPreview)
    {

        Character row = connection.FirstOrDefault<Character>(q => q.name == characterName && q.deleted == false);

        if (row != null)
        {
            // instantiate based on the class name
            Player prefab = prefabs.Find(p => p.name == row.classname);
            if (prefab != null)
            {
                GameObject go = Instantiate(prefab.gameObject);
                Player player = go.GetComponent<Player>();

                player.name = row.name;
                player.account = row.account;
                player.className = row.classname;
                Vector3 position = new Vector3(row.x, row.y, row.z);
                player.level.current = Mathf.Min(row.level, player.level.max); // limit to max level in case we changed it
                player.strength.value = row.strength;
                player.intelligence.value = row.intelligence;
                player.experience.current = row.experience;
                ((PlayerSkills)player.skills).skillExperience = row.skillExperience;
                player.gold = row.gold;
                player.isGameMaster = row.gamemaster;
                player.itemMall.coins = row.coins;

                // can the player's movement type spawn on the saved position?
                // it might not be if we changed the terrain, or if the player
                // logged out in an instanced dungeon that doesn't exist anymore
                //   * NavMesh movement need to check if on NavMesh
                //   * CharacterController movement need to check if on a Mesh
                if (player.movement.IsValidSpawnPoint(position))
                {
                    // agent.warp is recommended over transform.position and
                    // avoids all kinds of weird bugs
                    player.movement.Warp(position);
                }
                // otherwise warp to start position
                else
                {
                    Transform start = NetworkManagerMMO.GetNearestStartPosition(position);
                    player.movement.Warp(start.position);
                    // no need to show the message all the time. it would spam
                    // the server logs too much.
                    //Debug.Log(player.name + " spawn position reset because it's not on a NavMesh anymore. This can happen if the player previously logged out in an instance or if the Terrain was changed.");
                }

                LoadInventory(player.inventory);
                LoadEquipment((PlayerEquipment)player.equipment);
                LoadItemCooldowns(player);
                LoadSkills((PlayerSkills)player.skills);
                LoadBuffs((PlayerSkills)player.skills);
                LoadQuests(player.quests);
                LoadGuildOnDemand(player.guild);

                // assign health / mana after max values were fully loaded
                // (they depend on equipment, buffs, etc.)
                player.health.current = row.health;
                player.mana.current = row.mana;

                // set 'online' directly. otherwise it would only be set during
                // the next CharacterSave() call, which might take 5-10 minutes.
                // => don't set it when loading previews though. only when
                //    really joining the world (hence setOnline flag)
                if (!isPreview)
                {
                    row.online = true; row.lastsaved = DateTime.UtcNow;
                    connection.Update<Character>(row);
                }

                // addon system hooks
                onCharacterLoad.Invoke(player);

                return go;
            }
            else Debug.LogError("no prefab found for class: " + row.classname);
        }
        return null;
    }


    void SaveInventory(PlayerInventory inventory)
    {
        // inventory: remove old entries first, then add all new ones
        // (we could use UPDATE where slot=... but deleting everything makes
        //  sure that there are never any ghosts)
        connection.DeleteAll<CharacterInventory>(d => d.character == inventory.name);
        for (int i = 0; i < inventory.slots.Count; ++i)
        {
            ItemSlot slot = inventory.slots[i];
            if (slot.amount > 0) // only relevant items to save queries/storage/time
            {
                connection.Insert(new CharacterInventory{
                    character = inventory.name,
                    slot = i,
                    name = slot.item.name,
                    amount = slot.amount,
                    durability = slot.item.durability,
                    summonedHealth = slot.item.summonedHealth,
                    summonedLevel = slot.item.summonedLevel,
                    summonedExperience = slot.item.summonedExperience
                });
            }
        }
    }

    void SaveEquipment(PlayerEquipment equipment)
    {
        // equipment: remove old entries first, then add all new ones
        // (we could use UPDATE where slot=... but deleting everything makes
        //  sure that there are never any ghosts)
        connection.DeleteAll<CharacterEquipment>(d => d.character == equipment.name);
        for (int i = 0; i < equipment.slots.Count; ++i)
        {
            ItemSlot slot = equipment.slots[i];
            if (slot.amount > 0) // only relevant equip to save queries/storage/time
            {
                connection.Insert(new CharacterEquipment{
                    character = equipment.name,
                    slot = i,
                    name = slot.item.name,
                    amount = slot.amount,
                    durability = slot.item.durability,
                    summonedHealth = slot.item.summonedHealth,
                    summonedLevel = slot.item.summonedLevel,
                    summonedExperience = slot.item.summonedExperience
                });
            }
        }
    }

    void SaveItemCooldowns(Player player)
    {
        // equipment: remove old entries first, then add all new ones
        // (we could use UPDATE where slot=... but deleting everything makes
        //  sure that there are never any ghosts)
        connection.DeleteAll<CharacterItemCooldown>(d => d.character == player.name);
        foreach (KeyValuePair<string, double> kvp in player.itemCooldowns)
        {
            // cooldownEnd is based on NetworkTime.time, which will be different
            // when restarting the server, so let's convert it to the remaining
            // time for easier save & load
            // note: this does NOT work when trying to save character data
            //       shortly before closing the editor or game because
            //       NetworkTime.time is 0 then.
            float cooldown = player.GetItemCooldown(kvp.Key);
            if (cooldown > 0)
            {
                connection.Insert(new CharacterItemCooldown{
                    character = player.name,
                    category = kvp.Key,
                    cooldownEnd = cooldown
                });
            }
        }
    }

    void SaveSkills(PlayerSkills skills)
    {
        // skills: remove old entries first, then add all new ones
        connection.DeleteAll<CharacterSkill>(d => d.character == skills.name);
        foreach (Skill skill in skills.skills)
            if (skill.level > 0) // only learned skills to save queries/storage/time
            {
                // castTimeEnd and cooldownEnd are based on NetworkTime.time,
                // which will be different when restarting the server, so let's
                // convert them to the remaining time for easier save & load
                // note: this does NOT work when trying to save character data
                //       shortly before closing the editor or game because
                //       NetworkTime.time is 0 then.
                connection.Insert(new CharacterSkill{
                    character = skills.name,
                    name = skill.name,
                    level = skill.level,
                    castTimeEnd = skill.CastTimeRemaining(),
                    cooldownEnd = skill.CooldownRemaining()
                });
            }
    }

    void SaveBuffs(PlayerSkills skills)
    {
        // buffs: remove old entries first, then add all new ones
        connection.DeleteAll<CharacterBuff>(d => d.character == skills.name);
        foreach (Buff buff in skills.buffs)
        {
            // buffTimeEnd is based on NetworkTime.time, which will be different
            // when restarting the server, so let's convert them to the
            // remaining time for easier save & load
            // note: this does NOT work when trying to save character data
            //       shortly before closing the editor or game because
            //       NetworkTime.time is 0 then.
            connection.Insert(new CharacterBuff{
                character = skills.name,
                name = buff.name,
                level = buff.level,
                buffTimeEnd = buff.BuffTimeRemaining()
            });
        }
    }

    void SaveQuests(PlayerQuests quests)
    {
        // quests: remove old entries first, then add all new ones
        connection.DeleteAll<CharacterQuest>(d => d.character == quests.name);
        foreach (Quest quest in quests.quests)
        {
            connection.Insert(new CharacterQuest{
                character = quests.name,
                name = quest.name,
                progress = quest.progress,
                completed = quest.completed
            });
        }
    }

    // adds or overwrites character data in the database
    public void CharacterSave(Player player, bool online, bool useTransaction = true)
    {
        // only use a transaction if not called within SaveMany transaction
        if (useTransaction) connection.BeginTransaction();

        Character character = new Character
        {
            name = player.name,
            account = player.account,
            classname = player.className,
            x = player.transform.position.x,
            y = player.transform.position.y,
            z = player.transform.position.z,
            level = player.level.current,
            health = player.health.current,
            mana = player.mana.current,
            strength = player.strength.value,
            intelligence = player.intelligence.value,
            experience = player.experience.current,
            skillExperience = ((PlayerSkills)player.skills).skillExperience,
            gold = player.gold,
            coins = player.itemMall.coins,
            gamemaster = player.isGameMaster,
            online = online,
            lastsaved = DateTime.UtcNow
        };

        if (connection.FirstOrDefault<Character>(f => f.name == character.name) == null) connection.Insert<Character>(character);
        else connection.Update<Character>(character);

        SaveInventory(player.inventory);
        SaveEquipment((PlayerEquipment)player.equipment);
        SaveItemCooldowns(player);
        SaveSkills((PlayerSkills)player.skills);
        SaveBuffs((PlayerSkills)player.skills);
        SaveQuests(player.quests);
        if (player.guild.InGuild())
            SaveGuild(player.guild.guild, false); // TODO only if needs saving? but would be complicated

        // addon system hooks
        onCharacterSave.Invoke(player);

        if (useTransaction) connection.Transaction.Commit();
    }

    // save multiple characters at once (useful for ultra fast transactions)
    public void CharacterSaveMany(IEnumerable<Player> players, bool online = true)
    {
        connection.BeginTransaction(); // transaction for performance
        foreach (Player player in players)
            CharacterSave(player, online, false);
        connection.Transaction.Commit(); // end transaction
    }

    // guilds //////////////////////////////////////////////////////////////////
    public bool GuildExists(string guild)
    {
        return connection.FirstOrDefault<GuildInfo>(q => q.name == guild) != null;
    }

    Guild LoadGuild(string guildName)
    {
        Guild guild = new Guild();

        // set name
        guild.name = guildName;

        // load guild info
        GuildInfo info = connection.FirstOrDefault<GuildInfo>(q => q.name == guildName);
        if (info != null)
        {
            guild.notice = info.notice;
        }

        Debug.Log("List: " + connection.Select<CharacterGuild>(q => q.guild == guildName));

        // load members list
        var rows = connection.Select<CharacterGuild>(q => q.guild == guildName).ToArray();
        GuildMember[] members = new GuildMember[rows.Count()]; // avoid .ToList(). use array directly.
        Debug.Log("CharacterGuild Rows: " + rows.Count());
        for (int i = 0; i < rows.Count(); ++i)
        {
            CharacterGuild row = rows[i];

            GuildMember member = new GuildMember();
            member.name = row.character;
            Debug.Log("Row Character: " + row.character);
            member.rank = (GuildRank)row.rank;

            // is this player online right now? then use runtime data
            if (Player.onlinePlayers.TryGetValue(member.name, out Player player))
            {
                member.online = true;
                member.level = player.level.current;
            }
            else
            {
                member.online = false;
                // note: FindWithQuery<characters> is easier than ExecuteScalar<int> because we need the null check
                Character character = connection.FirstOrDefault<Character>(q => q.name == member.name);
                member.level = character != null ? character.level : 1;
            }

            members[i] = member;
        }
        guild.members = members;
        return guild;
    }

    public void SaveGuild(Guild guild, bool useTransaction = true)
    {
        if (useTransaction) connection.BeginTransaction(); // transaction for performance

        // guild info
        if (connection.FirstOrDefault<GuildInfo>(q => q.name == guild.name) != null)
        {
            connection.Update<GuildInfo>(new GuildInfo
            {
                name = guild.name,
                notice = guild.notice
            });
        } 
        else
        {
            connection.Insert<GuildInfo>(new GuildInfo
            {
                name = guild.name,
                notice = guild.notice
            });
        }

        // members list
        connection.DeleteAll<CharacterGuild>(d => d.guild == guild.name);
        foreach (GuildMember member in guild.members)
        {
            connection.Insert(new CharacterGuild{
                character = member.name,
                guild = guild.name,
                rank = (int)member.rank
            });
        }

        if (useTransaction) connection.Transaction.Commit(); // end transaction
    }

    public void RemoveGuild(string guild)
    {
        connection.BeginTransaction(); // transaction for performance
        connection.DeleteAll<GuildInfo>(d => d.name == guild);
        connection.DeleteAll<CharacterGuild>(d => d.guild == guild);
        connection.Transaction.Commit(); // end transaction
    }

    // item mall ///////////////////////////////////////////////////////////////
    public List<long> GrabCharacterOrders(string characterName)
    {
        // grab new orders from the database and delete them immediately
        //
        // note: this requires an orderid if we want someone else to write to
        // the database too. otherwise deleting would delete all the new ones or
        // updating would update all the new ones. especially in sqlite.
        //
        // note: we could just delete processed orders, but keeping them in the
        // database is easier for debugging / support.
        List<long> result = new List<long>();
        List<CharacterOrders> rows = connection.Select<CharacterOrders>(q => q.character == characterName && q.processed == false).ToList();
        foreach (CharacterOrders row in rows)
        {
            result.Add(row.coins);
            row.processed = true;
            connection.Update<CharacterOrders>(row);
        }
        return result;
    }

    //==============================================================================================================================================
    // Models

    [Alias("accounts")]
    [Schema(databaseName)]
    public class Account
    {
        [PrimaryKey()]
        public string name { get; set; }
        public string password { get; set; }
        // created & lastlogin for statistics like CCU/MAU/registrations/...
        public DateTime created { get; set; }
        public DateTime lastlogin { get; set; }
        public bool banned { get; set; }
    }

    [Alias("characters")]
    [Schema(databaseName)]
    public class Character
    {
        [PrimaryKey()]
        public string name { get; set; }
        [Index()] // add index on account to avoid full scans when loading characters
        public string account { get; set; }
        public string classname { get; set; } // 'class' isn't available in C#
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public int level { get; set; }
        public int health { get; set; }
        public int mana { get; set; }
        public int strength { get; set; }
        public int intelligence { get; set; }
        public long experience { get; set; } // TODO does long work?
        public long skillExperience { get; set; } // TODO does long work?
        public long gold { get; set; } // TODO does long work?
        public long coins { get; set; } // TODO does long work?
        public bool gamemaster { get; set; }
        // online status can be checked from external programs with either just
        // just 'online', or 'online && (DateTime.UtcNow - lastsaved) <= 1min)
        // which is robust to server crashes too.
        public bool online { get; set; }
        public DateTime lastsaved { get; set; }
        public bool deleted { get; set; }
    }

    [Alias("character_buffs")]
    [Schema(databaseName)]
    [CompositeIndex(true, new []{"character","name"})]
    public class CharacterBuff
    {
        public string character { get; set; }
        public string name { get; set; }
        public int level { get; set; }
        public float buffTimeEnd { get; set; }
    }

    [Alias("character_guild")]
    [Schema(databaseName)]
    public class CharacterGuild
    {
        // guild members are saved in a separate table because instead of in a
        // characters.guild field because:
        // * guilds need to be resaved independently, not just in CharacterSave
        // * kicked members' guilds are cleared automatically because we drop
        //   and then insert all members each time. otherwise we'd have to
        //   update the kicked member's guild field manually each time
        // * it's easier to remove / modify the guild feature if it's not hard-
        //   coded into the characters table
        [PrimaryKey()] // important for performance: O(log n) instead of O(n)
        public string character { get; set; }
        // add index on guild to avoid full scans when loading guild members
        [Index()]
        public string guild { get; set; }
        public int rank { get; set; }
    }

    [Alias("character_inventory")]
    [Schema(databaseName)]
    [CompositeIndex(true, new []{"character","slot"})]
    public class CharacterInventory
    {
        public string character { get; set; }
        public int slot { get; set; }
        public string name { get; set; }
        public int amount { get; set; }
        public int durability { get; set; }
        public int summonedHealth { get; set; }
        public int summonedLevel { get; set; }
        public long summonedExperience { get; set; }
    }

    [Alias("character_equipment")]
    [Schema(databaseName)]
    [CompositeIndex(true, new[] { "character", "slot" })]
    class CharacterEquipment : CharacterInventory { } // same layout

    [Alias("character_itemcooldowns")]
    [Schema(databaseName)]
    class CharacterItemCooldown
    {
        [PrimaryKey()] // important for performance: O(log n) instead of O(n)
        public string character { get; set; }
        public string category { get; set; }
        public float cooldownEnd { get; set; }
    }

    [Alias("character_orders")]
    [Schema(databaseName)]
    public class CharacterOrders
    {
        [PrimaryKey()] // important for performance: O(log n) instead of O(n)
        public int orderid { get; set; }
        public string character { get; set; }
        public long coins { get; set; }
        public bool processed { get; set; }
    }

    [Alias("character_quests")]
    [Schema(databaseName)]
    [CompositeIndex(true, new []{"character","name"})]
    class CharacterQuest
    {
        public string character { get; set; }
        public string name { get; set; }
        public int progress { get; set; }
        public bool completed { get; set; }
    }

    [Alias("character_skills")]
    [Schema(databaseName)]
    [CompositeIndex(true, new []{"character","name"})]
    public class CharacterSkill
    {
        public string character { get; set; }
        public string name { get; set; }
        public int level { get; set; }
        public float castTimeEnd { get; set; }
        public float cooldownEnd { get; set; }
    }

    [Alias("guild_info")]
    [Schema(databaseName)]
    public class GuildInfo
    {
        // guild master is not in guild_info in case we need more than one later
        [PrimaryKey()] // important for performance: O(log n) instead of O(n)
        public string name { get; set; }
        public string notice { get; set; }
    }
}
