# Friday Facts #309 - Controversial opinions

- Источник: https://factorio.com/blog/post/fff-309
- Авторы: Twinsen, wheybags, TOGoS, posila, Rseding
- Дата: 2019-08-23
- Темы: геймдизайн
- Применимость к проекту: средняя
- Что извлечь: Спорные мнения разработчиков о собственных механиках: что они изменили бы, если бы не привычки игроков.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
The boring phase of bug-fixing is still going, slowly but surely. Stable should be released next week, but with some people on vacation (Ben, Jitka, kovarex, Klonan, Sanqui) and with the release of WoW Classic, it might get slowed down a bit. (By the way, some of us will be playing on Pyrewood Village, Alliance, so if you want to have the chance of meeting Twinsen, kovarex or dominik while leveling, you can join that server).

So since there's not much happening, this week we decided to explore some unpopular or controversial opinions about the game from within the team.

In Wube we don't have a very strict management structure, everyone is free to have ideas and opinions about almost all aspects of the game. This means that with almost every change we argue and discuss a lot before making a final decision. Sometimes we argue about everything, from the smallest GUI change, to how a major feature should work. This is probably not a bad thing since this means changes are usually well thought out and unpopular ideas or changes don't make it to the game very often. Some people feel quite strongly about their opinions or sometimes the team is very divided on what should we do. Today we'll share some of those opinions and controversies.

**Keep in mind that these are simply opinions and none of them will actually make it into the game, we are simply sharing them to have an interesting discussion.**

## Inserters should not chase items Twinsen

What I always liked about Factorio is that it's a very precise game. Ratios, throughputs, crafting times are all well explained and even if it gets complicated, everything can be calculated to the last item. Miners mine resources at a precise rate, smelters and assemblers craft items at a precise speed, belts move the items at a precise throughput. But inserters... due to their behavior where they track items on the belt before grabbing them, they don't have a precise throughput. This really grinds my Iron gear wheels, as it's hard to even know if an inserter will be fast enough to load a simple assembler. There are [Wiki Tables](https://wiki.factorio.com/Inserters) and [References](https://factoriocheatsheet.com/#inserter-throughput), but they are ridiculously complex since it depends on the inserter type, source, destination, belt type, belt orientation and inserter capacity bonus. Even after accounting for all that, the numbers are still not precise since it depends on timings and how items are placed on the belt.

My proposition is to remove this complex movement of item chasing and have inserters move to their predefined pickup location and immediately pick up the closest item (in the case of a belt).
This will:

- Make inserters have a constant throughput. It will still depend on some factors like stack size (e.g. stack inserter will pick up faster from chest than from belt), but it should simplify things significantly and allow us to have a calculated throughput for chests and belts that we can show in inserter tooltips.

- Prevent all the issues with inserters getting stuck (e.g. burner inserters running out of fuel because they chase items they can not grab). We often argue if this is a bug or not and if it should be fixed.

- Improve performance (maybe significantly). Inserters currently chase items throughout it's entire swing, tracking the items on the belt each tick and searching for a new item each time one goes outside the belt (which happens quite often for blue belts). Removing this requirement would mean less memory access to other entities and much simpler mathematical calculations. Since inserters are the second most common built entity, these improvements could lead to a big UPS boost.

But on the other hand, this will mean:

- It will graphically look much worse. While I believe we can do many things to make this look good enough (by separating drawing animation logic from game logic), it will probably not look as nice as it does now.

- It will remove some of the nice organic feel of the game. And it will remove some (arguably) fun emergent situations.

Combine this with the fact that changing something so core to Factorio in such a significant way would have big implications. So we decided this wont be done.

## Blueprint import/export should be a modded feature Twinsen

The blueprint library has always been a controversial topic. We argued about how it should work technically, in multiplayer or if it should exist at all. But one thing that I particularly don't like is blueprint import/export. It's obvious that it's more fun to make your own setups and it's probably universally agreed within the team that importing large sections of factories is not what players should do. This promotes "instant gratification", it eliminates the satisfaction you feel after finishing your creation you spent hours tinkering and planning. And could easily make a 100-hour game into an 10-hour game. It's sad when we release an update and the first comment is "does anyone have the blueprint string for the nuclear reactors" or someone immediately asks for [an artillery shell production blueprint](https://steamcommunity.com/app/427520/discussions/0/2906376154331772625/).

It's our job as developers to incentivise the player to play the game properly. If importing blueprints is obviously so bad, why are we providing this tool as a vanilla tool? Not only that but we also place it in the "tools" section, as a shortcut in the main screen, as if you are expected to use it often.

So my proposition is that this tool should only be modded in. By making it a mod it's clearly not a an intended way to play.
The very common counter argument is "since it's obviously not fun, players won't use it. Put it in and let the players decide". That's like putting a "level up" button in an RPG and saying "well it's obviously not fun, it skips content, players won't use it, but let them decide". Yes they will, as soon as they encounter a problem a player will evaluate his options. A tempting "skip-ahead" button is irresistible in such situations.

It's hard to remove such a powerful tool now, since it's oh-so-tempting to import that nice blueprint book with belt balancers, so you don't have to deal with designing those complex things yourself. Until a different consensus is reached, the tool will stay in game. But I urge players to restrain themselves from adding too many blueprints to their library that are not their own (or their friend's).

## Combat/Biters wheybags

### Weapons shouldn't lock on

Weapon lock on and the distinction between shooting with spacebar and C is a constant source of confusion for new players. Also, it is just weird and not even fun. Weapons should all work like the shotgun, you point and shoot, and the bullets hit whatever is in that direction. There's only one button to shoot, and that can be spacebar. We could take inspiration from the huge number of top down shooters out there to figure out satisfying weapon mechanics that would work with this new style. Even if you don't like this style, we already have it for some weapons (shotgun), and using it for some weapons but not all is inconsistent and confusing. (Klonan made a [mod](https://mods.factorio.com/mod/KS_Combat) a while ago which does something like this).

### Biters should be more aggressive, and probe your defenses

Currently, you can get by with only walling off part of your base, responding to attacks as they come, and leaving the rest fairly open. This runs opposite to what "most new players" [citation needed] would expect. For example, I always started off by walling and turreting my entire base, presuming that if the biters noticed a part of my perimeter that was uncovered, they would just go through there. The counter-argument is that it makes the game less fun, because you spend way too long building massive defensive walls instead of making your factory. My counter to that is that for a lot of people, making defenses is very fun, everyone loves turtling in RTS games. Maybe it's just my play style, but this is what I was doing anyway even when it's not necessary, and leaving big holes in my walls just feel wrong. IMO if you don't enjoy that, you should just turn off biters. Also related is the way that biters will sometimes ignore your structures, if they are not polluting or military. For the same reasons, I think you should have to defend your railway lines, power poles, etc.

### Clearing bases should not leave you safe

Related to the point above, I think that clearing the bases inside your pollution cloud should provide only a very temporary respite from attacks. Biters should very regularly expand back into your pollution cloud, meaning you always have to defend. Alternatively, the whole pollution cloud mechanic could be removed, and replaced with a worldwide pollution count.

## Miners shouldn't output directly to belts wheybags

Every other interaction between a belt and a building uses an inserter to move the items to/from the belt. Why are miners any different? From a pure consistency point of view, I think this should be removed. As far as I know, the reason it has been left this way so far is just convenience. (Bonus fact: I used to put inserters in front of my miners for an unspecified-but-way-too-long time when I was new to the game).

## Boilers shouldn't have a water output wheybags

During our in-house testing, one of the things we did was invite people who had never played the game before into the office to try the new introduction campaign, while we observed them. One stumbling block that almost every person hit, was connecting their steam engine to the water output of their boiler, instead of the steam output. I understand that the current setup allows for some interesting layouts, but IMO it is not worth the usability cost.

## Pipes should work like electricity wheybags

Pipes and fluid have been a constant annoyance for a long time. Thanks to the labours of another of our developers (Dominik), this has improved greatly, and is due to improve even more. However, if I had the choice, I would just remove the headaches outright, by making pipes function like magic liquid teleporters. Each "pipe network" (a block of connected pipes) would have a set of inputs and outputs. The load would be drawn equally from all inputs, and distributed equally to all outputs. Of course, this would mean that there would be no flow limits for pipes. So you could, for example, have a massive array of offshore pumps connected via a single pipe to a large nuclear plant, and it would work just fine. There would also be no way to visualise the flow, so the windows would have to be removed. IMO, the cost is worth the gain.

## Adventure mode TOGoS

I can't get my wife to play Factorio with me because she's not really that into Factory building.
But she and I have been playing Satisfactory because it gives her
reasons to explore the map and discover new places and collect items.

The obvious change that could be made to Factorio
to accomodate that play style would be to scatter useful items
around the map.
I imagine things like an abandoned railyard
that would give one early access to a few prebuilt engines
and rails, totally changing the way an early factory is designed.

But there is a second aspect leads to Factorio not being very exploration-driven,
and that is that *you can see ridiculously far and nothing obstructs your line of sight*.
Who needs to explore when you can just plop down a few radar towers?
Forests would be much more meaningful if you couldn't see through them,
and had to actually poke around and through them to know what was there.
(I personally take inspiration from [Ultima 3](https://ultima.fandom.com/wiki/Ultima_III:_Exodus)
for line-of-sight simulation in 2D games).

And speaking of line-of-sight-blocking terrain features, we also need mountains.
Not just rows of cliffs, but large and impassible
mountain ranges that compliment the impassible (but non-sight-blocking and land-fillable)
bodies of water.

Games with a first-person perspective automatically give you the line-of-sight effect.
Another thing that true 3D game engines give you out of the box that 2D ones
usually don't is the possibility of non-planar worlds.
Bridges and tunnels can provide alternate (or secret) routes and interesting loops.
But it is possible to get some of the same effects out of a 2D engine.
Since Factorio already supports multiple surfaces, it wouldn't be too much of a stretch
to add a cave system (preferrably with an [odd topology](http://www.nuke24.net/plog/21.html)) underneath the surface surface,
with entrances in mountain sides.

I'm not sure that making vanilla Factorio more fun for people
who want to focus more on exploration is itself controversial,
but all these features take time and energy to build and maintain,
and "Factorio isn't supposed to be about exploration",
so it hasn't been a priority.

## Robots should take up space and time TOGoS

In my estimation, the main cause of bots becoming
[overpowered in late-stage factories](https://www.factorio.com/blog/post/fff-224)
is that they take up zero space
(large amounts of logistic or construction bots can occupy a single point on the map)
and take zero time to pick up or drop items or to deconstruct things.
This leads to the following:

- Laser-turret-creeping enemy bases is trivial because construction bots can instantaneously place enough turrets to completely overwhelm all the biters.

- There is no reason to mess with belts at all when bots can 'teleport' unlimited resources between storage chests.

Buildings taking time to construct (such as they do in games like Total Annihilation)
would to some degree solve the first problem,
since an already dug-in force would have a chance
to defend itself while turrets are being built.

Bots taking up space (so that they have to queue up to pick up and drop off items)
would solve the problem of bots completely obsoleting belts;
to transport large numbers of items quickly you would
need to spread out the pickup and load boxes
to give the many robots space to work.

The main reason we're not doing this is that bot movement logic
is currently written such that calculations are needed only when something changes
(a robot completes its task, its target is moved, etc).
Having to keep bots separated would add a lot more calculations
that need to be done while a bot is en route,
which would probably result in a lot of complaints
from people with huge bot-based factories
that the game got all slow.

## Items should have volume and mass TOGoS

Similar to the overpowerdness-of-bots problem,
being able to carry multiple locomotives (or rocket silos)
around in a backpack or in a train car feels silly.
Big things should require time and space to move around.

Solving that would require completely changing how construction works
—large things would have to be built on-site a la Total Annihilation or Satisfactory
instead of being preassembled.
But that could lead to its own interesting situations.
Some objects would require more resources than can fit in a standard stack,
so special assembly buildings would be needed.

There are mods
(such as [Rocket Silo Construction](https://mods.factorio.com/mod/Rocket-Silo-Construction))
that try to do this with certain buildings already.

## Power-user hotkeys posila

I believe it is alright for a game to have power-user controls that are not bound to any keys by default. For example "Toggle exoskeleton" introduced with a 0.17's toolbar is a feature that most people probably will never need, and lot of people won't even realize it exists. It is probably useful for some people, and it also useful that it can be toggled by a hotkey, but it doesn't need to be bound to any keys by default. People just get confused why they run slow when they press the hotkey by accident, even if they knew about the feature, but don't actively consider its existence.

I think if we changed our attitude towards default keybindings we could go nuts with adding new shortcuts and there would be little bit for everyone. I mean, how many people use hotkeys for connecting or disconnecting trains, and how many people would use a shortcut for toggling manual driving in trains.

## Mining furnaces and assembling machines should return the ingredients for the in-progress recipe Rseding

In Factorio the game makes a large effort to not punish the player for experimenting: you can mine up anything you build and move it somewhere else or just try different setups. The game doesn't punish the player for this (or so most people think). You can hand craft things and if you change your mind just stop crafting to get the ingredients back.

When it comes to furnaces and assembling machines this is different. Anything which is in-progress when you mine a furnace or assembling machine is lost. If you had a Kovarex enrichment process running with 40 Uranium 235 and you mine the centrifuge the game deletes all 40 of the Uranium 235 and you're left with nothing.

In a previous version of the game it would just give back everything it had consumed when the recipe started. However, because of a possible **incredibly** tiny exploit related to productivity modules and being able to get an extra % of items back if you time it correctly this was removed ([bug report](https://forums.factorio.com/57452)).

We continue to get bug reports about deleted items and all of them get filed under "not a bug". There have even been [mods](https://mods.factorio.com/mod/Dont_lose_in_progress_ingredients) created to address this specific shortcoming in the game.

Remember that since we are focusing on 1.0, these ideas are currently nothing more than a cause for heated discussions in our office. But let us know what you think (on our [forum](https://forums.factorio.com/74876))  and maybe we will change our mind based on the feedback. (maybe after 1.0...)

                [Discuss on our forums](https://forums.factorio.com/74876)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/cudu5n/friday_facts_309_controversial_opinions/)
