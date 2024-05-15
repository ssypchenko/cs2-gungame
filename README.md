<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>cs2-gungame</title>
</head>
<body>

<h1>cs2-gungame</h1>
<h2>GunGame for Counter-Strike 2</h2>

<p>GunGame is a gameplay plugin inspired by the SourceMode GunGame plugin. <a href="https://forums.alliedmods.net/showthread.php?t=93977">Original Plugin Thread</a>.</p>

<h2>Description</h2>
<p>GunGame challenges players with various weapons, requiring kills with each to progress. Players start with one weapon and must eliminate opponents to advance through the weapon sequence and ultimately win the game.</p>

<h2>Commands and Cvars</h2>
<p><em>Note: CVars are still in development by CounterStrikeSharp.</em></p>

<p>Commands available now:</p>
<ul>
    <li><code>gg_restart</code> - Restarts the whole game from the beginning.</li>
    <li><code>gg_enable</code> - Turn on gungame and restart the game.</li>
    <li><code>gg_disable</code> - Turn off gungame and restart the game.</li>
    <li><code>gg_reset</code> - Reset all gungame stats.</li>
    <li><code>gg_config &lt;foldername&gt;</code> - Request to start gungame with settings in a different folder.</li>
    <li><code>gg_respawn &lt;value&gt;</code> - Switch between behaviour if the players respawns are managed by plugin or server. 0 - disabled, 1 - T only, 2 - CT only, 3 - Both teams, 4 - Deathmatch spawns.</li>
    <li><code>!top</code> - Show the top winners on the server.</li>
    <li><code>!rank</code> - Show your current place in stats.</li>
    <li><code>!music</code> - Turn Off or On all plugin sounds for the player.</li>
    <li><code>!lang ..</code> - Player can change language of plugin messages.</li>
</ul>
<blockquote>
    <p><strong>WARNING</strong><br>
    Only works with ISO codes e.g.: <code>!lang en</code> or <code>!lang lv</code> You need the corresponding localisation file with the same name (en.json and ru.json are included). If you add GeoLite2-Country.mmdb to cfg folder, plugin will detect the player language based on his IP address.</p>
</blockquote>

<h2>Requirements</h2>
<ul>
    <li>Counter-Strike 2</li>
    <li>Metamod:Source v1282+</li>
    <li>Counter Strike Share v.204+</li>
</ul>

<h2>Installation</h2>
<ol>
    <li>Install Metamod:Source and Counter Strike Sharp.</li>
    <li>Copy DLLs to <code>csgo/addons/counterstrikesharp/plugins/GG2</code>.</li>
    <li>Place config files in <code>csgo/cfg/gungame</code>.</li>
    <li>Place GeoLite2-Country.mmdb if you have it to <code>csgo/cfg</code></li>
</ol>
<p><em>Config Files:</em></p>
<ul>
    <li><code>weapons.json</code> - Weapon settings (modification not recommended).</li>
    <li><code>gungame.json</code> - Main settings with comments for guidance. Some of them marked as (it does not work now), keep that in mind.</li>
    <li><code>gungame_weapons.json</code> - Customizable weapon order.</li>
    <li><em>Additional system executable configs with in-file comments.</em></li>
</ul>

<h2>Translations</h2>
<p>Available in English and Russian.</p>

<h2>Upgrade</h2>
<p>Please read the release notes carefully for upgrade instructions.</p>

<h2>Development</h2>
<p>A GunGame API has been developed. Plugin developers can subscribe to GunGame events or request player data from the GunGame plugin.
API dlls are located in <code>csgo/addons/counterstrikesharp/shared/GunGameAPI</code> folder. API description will be available in the next release.</p>

<h2>TODO</h2>
<p>Future enhancements include:</p>
<ul>
    <li>Additional functionalities from the original plugin.</li>
    <li>I've already added at least one important part that was not in the original plugin: shooting and knife protection, which you can turn off if you don't like it.
    I'm going to improve this plugin a lot in the near future. However, it will not be distributed for free. I'll describe it later. These improvements include
        <ul>
            <li>leader and loser management (highlight leaders and losers)</li>
            <li>Team balance management</li>
            <li>Fair play settings to allow not only "professional" players to win,</li>
            <li>etc. (we have lots of ideas :)</li>
        </ul>
    </li>
</ul>

<h2>FAQ</h2>
<p><strong>Q: Why doesn't the map change after a win?</strong><br>
A: GunGame doesn't handle map changes; it triggers a command in <code>gungame.mapvote.cfg</code> for map voting.</p>

<p><strong>Q: What do I put for game_mode and game_type in CS2</strong><br>
A: Use <code>+game_type 0 +game_mode 0</code>.</p>

<p><strong>Q: What if something isn't working?</strong><br>
A: Feel free to ask on the Counter Strike Sharp Discord. Assistance will be provided, though fixes are not guaranteed.</p>

<p><strong>Q: Where can I find geo database for ip addresses - GeoLite2-Country.mmdb?</strong><br>
A: You can get it from: <a href="https://dev.maxmind.com/geoip/geolite2-free-geolocation-data">MaxMind GeoLite2 Free Geolocation Data</a>
Or from release</p>

<p><em>This README is a work in progress and will be updated as the plugin develops.</em></p>

<h2>Credits</h2>
<p>Special thanks to altex for the original plugin, aproxje for the ideas from Language Manager Plugin, the Counter Strike Sharp Discord community, and Chat-GPT for assistance, I hope it will remember how polite I was.</p>

<h2>Donations</h2>
<a href="https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=APGJ8MXWRDX94">
  <img src="https://www.paypalobjects.com/en_GB/i/btn/btn_donate_SM.gif" alt="Donate with PayPal" />
</a>
</body>
</html>
