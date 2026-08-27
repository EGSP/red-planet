# Friday Facts #337 - Statistics GUI and Mod Debugger

- Источник: https://factorio.com/blog/post/fff-337
- Авторы: Klonan, Oxyd, Justarandomgeek
- Дата: 2020-03-06
- Темы: интерфейс, данные, инструменты
- Применимость к проекту: высокая
- Что извлечь: Интерфейс статистики производства: сбор показателей по времени и их подача графиками. Отдельно — отладчик модов с пошаговым исполнением.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## Statistics GUI Klonan, Oxyd

The statistics GUI (electric network stats, production stats, etc.) is one of the GUIs that has been in the game for a very long time, and has had its functionality fleshed out reasonably over the years. It was not long ago when Twinsen added hovering and highlighting to the graphs.

Given that, and the relatively short timeframe for 1.0 release, the update of the statistics GUI has really just been a style update, no new features or heavy logic rewriting. Oxyd has most of the work done, so we are happy to show some real in-game screenshots of how it looks:

![](https://cdn.factorio.com/assets/img/blog/fff-337-electric-1.png)

A notable change with the electric stats is that the Satisfaction/Production/Accumulator charge are next to each other in a single row, as opposed to each in a separate row. The label for the exact amount has also been moved to inside of the progress bar, which itself is much thicker.

![](https://cdn.factorio.com/assets/img/blog/fff-337-production-1.png)

![](https://cdn.factorio.com/assets/img/blog/fff-337-production-3-filters.png)

The production stats are pretty much the same functionality wise. One new button you might spot is the search button.

![](https://cdn.factorio.com/assets/img/blog/fff-337-production-2-search.png)

However there are some problems with the search feature. As you can see, production and consumption frames have a different search box independent from each other. The main problem is when pressing CTRL+F to perform a regular search: How do we know which frame to open? Of course this could lead to different solutions like the use of a cycle for the focus of the search, in which the second time you press CTRL+F the other frame gets the focus. Or both of the search boxes open at the same time but only one gets the focus. Or only one frame gets the focus and the other one works only by pressing the button. But let's face it, these "solutions" are not solid at all and create inconsistency in the main design.

To solve this issue we decided that the simplest way to go is the use of just one search box on the header of the panel. This new location works as a general feature for the entire panel. One single search gives you 2 results, one on each frame. This solution is used in the new character window -to come soon- making it consistent with the whole design of the GUI.

![](https://cdn.factorio.com/assets/img/blog/fff-337-statistics-search.png)

You can also see we took this opportunity to integrate the Kill statistics in with the rest, instead of being its own window with its own hotkey.

The Statistics GUIs will need a few tweaks and polishings here and there before it is ready for release, but unless something unexpected happens you can expect it coming out in a release soon.

## Community spotlight - Mod Debugging and Instrument Mode justarandomgeek

*
This topic is a guest post by our community member and mod maker [justarandomgeek](https://mods.factorio.com/user/justarandomgeek).
*

Historically, while developing Factorio mods, you just had to write some code and try it, until you get one of these:

![](https://cdn.factorio.com/assets/img/blog/fff-337-oops.png)

Then you go back and find that spot in your code and try to figure out what went wrong. If the error is particularly confusing, you start sprinkling log() or game.print() around to try and figure out what the heck is going on. And, of course, when you find the problem, you inevitably forget to clean all of these up, and now you're spamming the log or chat forever.

![](https://cdn.factorio.com/assets/img/blog/fff-337-oops-caught.png)

*Well there's your problem! Perhaps a bit constructed...*

For the last few months I've been working on a debugger to improve this experience. The first step was to spend some time with the Lua debug library and VSCode's Debug Adapter Protocol making introductions to get them talking to each other. Factorio doesn't give me many options, but Lua's debug.debug() and print() functions are enough to interact with the (normally invisible) console.

[VSCode](https://code.visualstudio.com/) can launch Factorio and attach itself to this console to inject commands as needed to read and manipulate the state of the running code. This gets us VSCode's debugger interface, with all the pre-built tools for displaying variables, setting breakpoints and stepping through code. Wrapping some of the game's APIs like remote.call() and most of script to add a little extra special handling, we get nice labels on event handlers in the call stack and a first version of break-on-exception for when things go wrong. I even built some nice detailed variable views for the most common LuaObjects:

![](https://cdn.factorio.com/assets/img/blog/fff-337-variables.png)

But there's a catch, and it's a big one: because of the way Factorio sandboxes mods, the debugger (which is itself a mod, in part) can't actually get to your mod to install all these hooks! The first solution to this is to simply add a line to load the debugger, but this puts us right back where we started with log() - you put something in for debugging and forget to remove it.

To solve this problem we need a little help from the API, a way for a mod to hook every other mod. Unfortunately, this level of hooks is too powerful of a capability to be generally available to mods, so it can't really be added to the normal mod API (it allows you to fairly trivially break nearly all assumptions about data lifecycle and mod sandboxing). After some discussion with Rseding, we eventually arrived at [Instrument Mode](https://lua-api.factorio.com/latest/Instrument.html) (released with 0.18.10), a special mode which allows a selected mod to hook all the Lua instances Factorio creates, at the cost of disabling multiplayer and only allowing one Instrument at once. This also provides a hook for a better version of break-on-exception.

This gets us all the way to the rich debugger experience: When you run under the debugger, all loaded mods automatically get debug hooks installed for the session (but not permanently), and you can step through the code and examine all your variables!

While I was building tools, I also added some highlighters for locale and changelog files, and validation for changelogs:

![](https://cdn.factorio.com/assets/img/blog/fff-337-locale.png)

![](https://cdn.factorio.com/assets/img/blog/fff-337-changelog.png)

And of course, it's a game about automation, so we need some modding workflow automation too:

![](https://cdn.factorio.com/assets/img/blog/fff-337-packages.png)

Modders, please [give it a try](https://marketplace.visualstudio.com/items?itemName=justarandomgeek.factoriomod-debug) and let me know what you think!

                [Discuss on our forums](https://forums.factorio.com/82048)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/fefsx2/friday_facts_337_statistics_gui_and_mod_debugger/)
