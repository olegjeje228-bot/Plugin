import asyncio
import json
import os
import random
import time

import discord
from discord import app_commands
from discord.ext import tasks

# ==== ТОКЕН ====
TOKEN = "MTUzMjM3NzgxNTEzMzk4Mjc5MQ.GhIXV3.7N7ghGJYQwc4kdLWAH4NHp30ZYOsdHbcbUaClU"

FILE = "/home/scpsl/.config/EXILED/Configs/EventHUD-Online.json"
CMD_FILE = "/home/scpsl/.config/EXILED/Configs/EventHUD-BotCommand.txt"
GUILD_ID = 1523593525193605210
UPDATE_CHANNEL_ID = 1528700005001597031
ROLE_RR = 1526955080891236352
ROLE_SR = 1528728235855319080
BAN_ROLE_ID = 1525326720327221320
ban_last_use = 0.0

CALLADMIN_FILE = "/home/scpsl/.config/EXILED/Configs/EventHUD-CallAdmin.txt"
CALLADMIN_CHANNEL_ID = 1532373274674073620
CALLADMIN_ROLE_ID = 1525372694542024795
CALLADMIN_COLORS = [0xE74C3C, 0x3498DB, 0x2ECC71, 0x9B59B6, 0xF1C40F, 0xE67E22, 0x1ABC9C]

CMDLOG_FILE = "/home/scpsl/.config/EXILED/Configs/EventHUD-CommandLog.txt"
CMDLOG_CHANNEL_ID = 1529122708749684827

# ==== ТРАФИК ====
TRAFFIC_FILE = "/home/scpsl/.config/EXILED/Configs/EventHUD-Traffic.txt"
TRAFFIC_CHANNEL_ID = 1532202560105091182  # 0 = слать в UPDATE_CHANNEL_ID

# ==== НОРМА ====
# Канал, в котором разрешена команда /norma (0 = разрешить везде).
# Сам отчёт шлёт плагин вебхуком, а не бот.
NORMA_CHANNEL_ID = 1530606084115398798

INTERVAL = 15
STALE_AFTER = 60


def read_data():
    try:
        with open(FILE, encoding="utf-8") as f:
            data = json.load(f)
        age = time.time() - data.get("updated", 0)
        if age > STALE_AFTER:
            print(f"[debug] файл устарел: age={age:.0f}s > {STALE_AFTER}s, data={data}")
            return None
        return data
    except FileNotFoundError:
        print(f"[debug] файл не найден: {FILE}")
        return None
    except Exception as e:
        print(f"[debug] ошибка чтения: {e}")
        return None


def send_command(cmd: str):
    with open(CMD_FILE, "w", encoding="utf-8") as f:
        f.write(cmd)


def has_role(interaction: discord.Interaction, role_id: int) -> bool:
    roles = getattr(interaction.user, "roles", None) or []
    return any(r.id == role_id for r in roles)


class OnlineBot(discord.Client):
    def __init__(self):
        super().__init__(intents=discord.Intents.default())
        self.tree = app_commands.CommandTree(self)

    async def setup_hook(self):
        if GUILD_ID:
            guild = discord.Object(id=GUILD_ID)
            self.tree.copy_global_to(guild=guild)
            await self.tree.sync(guild=guild)
        else:
            await self.tree.sync()
        self.bg = asyncio.create_task(self.updater())

    async def updater(self):
        await self.wait_until_ready()
        last = None
        while not self.is_closed():
            data = read_data()
            text = f"Онлайн: {data['online']}/{data['max']}" if data else "Сервер оффлайн"
            if text != last:
                try:
                    await self.change_presence(activity=discord.CustomActivity(name=text))
                    last = text
                except Exception as e:
                    print("presence error:", e)
            await asyncio.sleep(INTERVAL)

    async def on_ready(self):
        print(f"Запущен как {self.user}")
        if not self.calladmin_watcher.is_running():
            self.calladmin_watcher.start()
        if not self.cmdlog_watcher.is_running():
            self.cmdlog_watcher.start()
        if not self.traffic_watcher.is_running():
            self.traffic_watcher.start()

    # ==== ТРАФИК: постим отчёты плагина в канал ====
    @tasks.loop(seconds=5)
    async def traffic_watcher(self):
        try:
            if not os.path.exists(TRAFFIC_FILE):
                return
            with open(TRAFFIC_FILE, "r", encoding="utf-8") as f:
                raw = f.read().strip()
            if not raw:
                return
            with open(TRAFFIC_FILE, "w", encoding="utf-8"):
                pass
            channel = self.get_channel(TRAFFIC_CHANNEL_ID or UPDATE_CHANNEL_ID)
            if channel is None:
                return
            for block in raw.split("-----"):
                block = block.strip()
                if block:
                    await channel.send(block[:1990])
        except Exception as e:
            print("traffic error:", e)

    @tasks.loop(seconds=5)
    async def cmdlog_watcher(self):
        try:
            if not os.path.exists(CMDLOG_FILE):
                return
            with open(CMDLOG_FILE, "r", encoding="utf-8") as f:
                lines = [l.strip() for l in f if l.strip()]
            if not lines:
                return
            with open(CMDLOG_FILE, "w", encoding="utf-8"):
                pass
            channel = self.get_channel(CMDLOG_CHANNEL_ID)
            if channel is None:
                return
            chunk = ""
            for line in lines:
                steamid, _, rest = line.partition("|")
                cmd, _, resp = rest.partition("|")
                entry = f"{steamid} {cmd} -> {resp}\n"
                if len(chunk) + len(entry) > 1900:
                    await channel.send(f"```{chunk}```")
                    chunk = ""
                chunk += entry
            if chunk:
                await channel.send(f"```{chunk}```")
        except Exception as e:
            print("cmdlog error:", e)

    @tasks.loop(seconds=3)
    async def calladmin_watcher(self):
        try:
            if not os.path.exists(CALLADMIN_FILE):
                return
            with open(CALLADMIN_FILE, "r", encoding="utf-8") as f:
                lines = [l.strip() for l in f if l.strip()]
            if not lines:
                return
            with open(CALLADMIN_FILE, "w", encoding="utf-8"):
                pass
            channel = self.get_channel(CALLADMIN_CHANNEL_ID)
            if channel is None:
                return
            for line in lines:
                name, _, reason = line.partition("|")
                embed = discord.Embed(
                    description=f"**Вызов Администрации**\n\nВас вызывает `{name}` с причиной: `{reason}`",
                    color=random.choice(CALLADMIN_COLORS),
                )
                await channel.send(
                    content=f"<@&{CALLADMIN_ROLE_ID}>",
                    embed=embed,
                    allowed_mentions=discord.AllowedMentions(roles=True),
                )
        except Exception as e:
            print("calladmin error:", e)


bot = OnlineBot()


@bot.tree.command(name="server", description="IP сервера, онлайн и статус раунда")
async def server_cmd(interaction: discord.Interaction):
    data = read_data()
    if data is None:
        await interaction.response.send_message("Сервер сейчас оффлайн.")
        return
    embed = discord.Embed(title="AceLine Events", color=0x2ECC71)
    embed.add_field(name="IP", value=f"{data.get('ip', '?')}:{data.get('port', '?')}", inline=False)
    embed.add_field(name="Онлайн", value=f"{data['online']}/{data['max']}", inline=True)
    embed.add_field(name="Раунд", value="идёт" if data.get("round") else "не идёт", inline=True)
    await interaction.response.send_message(embed=embed)


@bot.tree.command(name="serverstatus", description="Трафик сервера за 30 мин / 2 часа / 24 часа")
async def serverstatus_cmd(interaction: discord.Interaction):
    if read_data() is None:
        await interaction.response.send_message("Сервер оффлайн, статистики нет.", ephemeral=True)
        return
    try:
        send_command("serverstatus")
    except Exception as e:
        await interaction.response.send_message(f"Не удалось отправить команду: {e}", ephemeral=True)
        return
    await interaction.response.send_message("Запросил отчёт, он придёт в канал в течение ~10 секунд.", ephemeral=True)


@bot.tree.command(name="rr", description="Рестарт раунда")
async def rr_cmd(interaction: discord.Interaction):
    if not has_role(interaction, ROLE_RR):
        await interaction.response.send_message("Нет прав: нужна роль тех (1lvl).", ephemeral=True)
        return
    if read_data() is None:
        await interaction.response.send_message("Сервер оффлайн, рестартить нечего.", ephemeral=True)
        return
    try:
        send_command("rr")
    except Exception as e:
        await interaction.response.send_message(f"Не удалось отправить команду: {e}", ephemeral=True)
        return
    await interaction.response.send_message("отправлена команда rr")


@bot.tree.command(name="sr", description="Полный рестарт сервера")
async def sr_cmd(interaction: discord.Interaction):
    if not has_role(interaction, ROLE_SR):
        await interaction.response.send_message("Нет прав: нужна роль тех (2lvl).", ephemeral=True)
        return
    if read_data() is None:
        await interaction.response.send_message("Сервер оффлайн.", ephemeral=True)
        return
    try:
        send_command("sr")
    except Exception as e:
        await interaction.response.send_message(f"Не удалось отправить команду: {e}", ephemeral=True)
        return
    await interaction.response.send_message("Полный рестарт отправлен")


UPDATE_TYPES = {
    "bug":     ("🟥 Обнаружен баг",             0xE74C3C),
    "fixed":   ("🟩 Баг пофикшен",              0x2ECC71),
    "update":  ("🟦 Обновление",                0x3498DB),
    "events":  ("⚙️ Обновление Events",         0x9B59B6),
    "discord": ("🔷 Обновление дискорд канала", 0x5865F2),
    "tech":    ("🛠 Технические работы",        0xF1C40F),
}


@bot.tree.command(name="update", description="Опубликовать новость в канал обновлений")
@app_commands.default_permissions(administrator=True)
@app_commands.rename(utype="тип", text="текст")
@app_commands.describe(utype="Категория", text="Текст (пиши \\n для переноса строки)")
@app_commands.choices(utype=[
    app_commands.Choice(name="Обнаружен баг", value="bug"),
    app_commands.Choice(name="Баг пофикшен", value="fixed"),
    app_commands.Choice(name="Обновление", value="update"),
    app_commands.Choice(name="Обновление Events", value="events"),
    app_commands.Choice(name="Обновление дискорд канала", value="discord"),
    app_commands.Choice(name="Технические работы", value="tech"),
])
async def update_cmd(interaction: discord.Interaction, utype: app_commands.Choice[str], text: str):
    title, color = UPDATE_TYPES[utype.value]
    embed = discord.Embed(
        title=title,
        description=text.replace("\\n", "\n"),
        color=color,
        timestamp=discord.utils.utcnow(),
    )
    embed.set_footer(text="AceLine Events")
    channel = bot.get_channel(UPDATE_CHANNEL_ID) if UPDATE_CHANNEL_ID else interaction.channel
    if channel is None:
        await interaction.response.send_message("Канал не найден, проверь UPDATE_CHANNEL_ID.", ephemeral=True)
        return
    try:
        await channel.send(content=f"Написал {interaction.user.mention}", embed=embed)
    except Exception as e:
        await interaction.response.send_message(f"Не удалось отправить: {e}", ephemeral=True)
        return
    await interaction.response.send_message("Опубликовано", ephemeral=True)


@bot.tree.command(name="ban", description="Забанить на игровом сервере по IP или UserID")
@app_commands.rename(target="цель", minutes="минуты", reason="причина")
@app_commands.describe(target="IP или SteamID64 (7656...)", minutes="Срок в минутах (0 = навсегда)", reason="Причина бана")
async def ban_cmd(interaction: discord.Interaction, target: str, minutes: int = 0, reason: str = "Забанен через Discord"):
    global ban_last_use
    if not has_role(interaction, ROLE_SR):
        await interaction.response.send_message("Нет прав: нужна роль тех (2lvl).", ephemeral=True)
        return
    now = time.time()
    if now - ban_last_use < 2:
        await interaction.response.send_message("Подожди 2 секунды (кд).", ephemeral=True)
        return
    ban_last_use = now
    t = target.strip().replace("|", "")
    try:
        with open(CMD_FILE, "w", encoding="utf-8") as f:
            f.write(f"ban|{t}|{minutes}|{reason}")
    except Exception as e:
        await interaction.response.send_message(f"Ошибка: {e}", ephemeral=True)
        return
    srok = "навсегда" if minutes <= 0 else f"{minutes} мин"
    await interaction.response.send_message(f"🔨 Бан отправлен: `{t}` ({srok})", ephemeral=True)


# ============================================================
# НОВОЕ: команда /norma
# ============================================================
@bot.tree.command(name="norma", description="Отчёт по норме админов")
@app_commands.rename(days="дни")
@app_commands.describe(days="За сколько дней (можно дробно: 0.5). По умолчанию 3")
async def norma_cmd(interaction: discord.Interaction, days: float = 3.0):
    if NORMA_CHANNEL_ID and interaction.channel_id != NORMA_CHANNEL_ID:
        await interaction.response.send_message(
            f"Эту команду можно использовать только в <#{NORMA_CHANNEL_ID}>.",
            ephemeral=True,
        )
        return

    if days <= 0:
        await interaction.response.send_message("Дни должны быть больше нуля.", ephemeral=True)
        return

    if read_data() is None:
        await interaction.response.send_message(
            "Сервер оффлайн - отчёт считает плагин, поэтому нужен запущенный сервер.",
            ephemeral=True,
        )
        return

    try:
        send_command(f"norma|{days}")
    except Exception as e:
        await interaction.response.send_message(f"Не удалось отправить: {e}", ephemeral=True)
        return

    await interaction.response.send_message(
        "Запросил отчёт, он придёт вебхуком в течение ~5 секунд.",
        ephemeral=True,
    )


bot.run(TOKEN)