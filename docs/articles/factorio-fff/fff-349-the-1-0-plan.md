# Friday Facts #349 - The 1.0 plan

- Источник: https://factorio.com/blog/post/fff-349
- Авторы: Klonan, Rseding, Boskid
- Дата: 2020-05-29
- Темы: процесс, инструменты, данные
- Применимость к проекту: высокая
- Что извлечь: Сокращение объёма работ ради срока выпуска. Отдельно — обозреватель прототипов, показывающий все описания сущностей игры прямо в интерфейсе.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello, today we have some big news.

## The 1.0 plan Klonan

In [FFF-321](https://www.factorio.com/blog/post/fff-321) we announced a release date for version 1.0. Given recent events we have decided to make an amendment to the date of 1.0.
The new date we are aiming for is Friday August 14th 2020, which is 5 weeks earlier than the original date.

The main reason to change the release date is the release of Cyberpunk 2077.
In January this year, CD Projekt Red [announced a delay](https://twitter.com/CDPROJEKTRED/status/1217861009446182912) to the release of Cyberpunk 2077, to September 17th, 1 week before our Factorio 1.0 launch.
We think any release close to such a monumental game is going to feel some negative effects, such as everybody playing and covering Cyberpunk and taking attention away from other games.

So we thought it was best to try release either before Cyberpunk or quite a while after it.
Given the two choices, we opted to bring the release date forward. There are several reasons why we are choosing to release earlier:

### Descoped goal

When we announced the date ([FFF-321](https://www.factorio.com/blog/post/fff-321)), we had plans for many things to be in the final release. The main topics were the new campaign, fluid algorithm improvements, and the full GUI rewrite.
Due to independent reasons, we have cancelled the new campaign ([FFF-331](https://www.factorio.com/blog/post/fff-331)), postponed the fluid improvements, and cut a lot of the aspects of the GUI rewrite ([FFF-348](https://www.factorio.com/blog/post/fff-348)).

### Staying on schedule

Apart from descoping some features, the other work we've been doing has been progressing at a good pace. The 0.18 experimental release structure ([FFF-314](https://www.factorio.com/blog/post/fff-314)) is really helping to keep things on track.
The original estimate was made with some concession for delays, that "things always take longer than expected".
Well for the last 6 months, most things haven't been taking longer than expected, and we've been finishing topics quite effectively.

### The sooner the better

The general feeling in the office is that the game is pretty much done, and that we want to get it released as soon as possible.
The sooner we get some closure on version 1.0, the sooner we can start thinking about fun and exciting new things.

So due to the co-incidence of cancelling several major features, we can afford to bring the release date forward. To be clear, we didn't cancel or postpone any features due to the Cyberpunk release date.

This new release date gives us 10 weeks, and from this point until Friday August 14th, the main focus for the team is on finalising the game, updating the trailer, and preparing marketing materials.

[![](https://cdn.factorio.com/assets/img/blog/fff-327-2020-cover.jpg)

Click to view full resolution](https://cdn.factorio.com/assets/img/blog/fff-327-2020-cover.jpg)

## Locale plan and Locale freeze Klonan

One part of finalising the game comes in finalising the community translations of the game. For a long time we have used [Crowdin](https://crowdin.com/project/factorio) for sourcing all the translations. Crowding has worked really well, and is deeply integrated into our workflow ([FFF-48](https://www.factorio.com/blog/post/fff-48)).

However some languages are not 100% covered, and there has not been any overall proofreading. For this reason we chose to look for a professional translation company to help fill in the gaps and proofread everything. We specifically needed a company that will work through Crowdin, as the community there has years of experience with the game, and the system won't need any management on our side.

After consultation with many companies and many other game developers for their thoughts, we have decided to partner with [Altagram](https://altagram.com/), based in Berlin, Germany.

With the final GUI update finished, we have frozen the locale, which means no more additions or changes (as much as is reasonable). There will be a proofreading of the English source texts, and after that a proofreading of all the target languages.

For absolute clarity, Altagram has detailed the plan and the process from their perspective:

*

Once we get the greenlight that the community has completed their contributions to the translations in Crowdin, we’ll start proofreading the English source text to offer any grammar or stylistic improvements, as needed. Once the English is polished, we’ll get started on proofreading for the secondary languages.

The linguists, who are all gamers themselves, and experts in game localization, will work their magic to ensure that the target language is as true to the source text as possible, to ensure that all players of Factorio, regardless of the language they play in, will have the same experience.

Some things that the linguists will check for when proofreading the target language are: Ensuring that the text itself, especially all in-game terms, is consistent throughout; that spelling and grammar is correct; and that the translations carry the same meaning and emotion as intended.

After we offer our suggestions, the texts will be sent back to the community for their final approval before implementation into the game.

*

From now until 1.0 release, Locale freeze mainly means that we'll only be working on topics that don't require new strings, such as bugfixes, new graphics, sound design, etc.

## Prototype Explorer GUI/Prototypes GUI Rseding

### Inspiration

Factorio has a lot of debug features and tools built into it over the years. Some of them are used extensively (show FPS/UPS) and others we wonder how we ever did without (GUI style inspection tooltip). Every one of them was added for a purpose and then ended up providing far more utility than its original purpose. With that in mind, and because they also end up being a lot fun (to me); I was working on fixing an issue I found with the GUI style inspection tooltip logic and thought to myself: wouldn't it be nice to have something like this for all the prototypes in the game? Is that even something I could do realistically? How would it look, how would it handle all the nesting that happens... but it sounded fun.

And so I decided to see what utility such a thing could have:

Figure out if a mod has tweaked or changed something - or if it was supposed to and didn't (common in modded bug reports and during mod development)
Provide a place to extract information that the game doesn't show anywhere else (not everything is exposed through the mod API, and it's unrealistic to expect anyone to remember the entire API)
Link from game concepts to the wiki explaining more about them.

The list of benefits seemed worth at least tinkering with the idea.

![](https://cdn.factorio.com/assets/img/blog/fff-349-prototype-explorer.png)

### Technical design

The first part I needed to figure out was - how was I going to get everything shunted into a GUI. Factorio is written in C++ and C++ does not have reflection. There's no easy way to say "for all of the variables this thing has, do this". Really the only way to get everything covered is to send each thing to the GUI. It's not pretty, but we also don't make changes to prototypes frequently at this stage in development. Additionally, if something is "wrong" it doesn't cause crashing/errors; it's an easy fix that anyone can do. A lot of boring typing later that part was covered.

### Nothing is ever easy or simple

For each thing to show: show the name, show the value, show the type. It sounded simple but it never is.

The type can be incredibly verbose and or just useless to a human: what does this even mean? "class std::basic_string,class std::allocator >" (it's a string...)
The value can be huge - so collapsing needed to be created
The value can be an array of something, so "empty" should be shown for empty arrays
Arrays of things with 1 value really should just show the 1 value
Optional things should show "empty" when not set
Some things will link to another thing so linking needed to be create
All of this has to work at any nesting level

But that's Factorio, and the polish is what makes it nice to use. I don't regret any of it.

### Wiki linking

Near the early stages of development I decided that the easiest way to convey to anyone using this what some "type" is and how it's supposed to be used is to show the wiki page about it. The wiki has very detailed information about a lot of what this was going to be showing and it seemed only logical to utilize it. But I didn't want to hard-code links... that never ends well.

My idea: a page on the wiki that provides a mapping of game type -> wiki URL. The game would download the mapping and as it filled in the GUI if it found a type that existed in the mapping it would link it to the wiki. Bilka got the wiki side of it quickly setup. The game side... "nothing is ever easy or simple".

I didn't want to download the wiki page every time the GUI opened - that would be a waste
I didn't want to download the wiki page every time the game launched - the page wouldn't change frequently and so would be a waste
But the page still needed to be re-downloaded when it changed
I didn't want the game to pause while the download ran
It needed to be fault tolerant (not everyone has an internet connection, or can even say the page will download correctly)

And so, a simple "link the type to the wiki" turned into:

It remembers the last time it downloaded the wiki page and what the revision ID was
It only tries to download the revision ID the first time one of the GUIs is opened
If the revision ID changed, or it didn't have the mapping locally, it tries to download the latest wiki mapping
If the download succeeds, it saves it for next time and populates the GUIs links
If any of this fails it logs what went and silently continues running (this is a the internet after all, random failure is expected)
It all happens in a background thread so the game doesn't pause while this logic works

And it all works perfectly.

![](https://cdn.factorio.com/assets/img/blog/fff-349-prototype-details.png)

## Native Lua serialisation Boskid

Last week I was requested by Rseding to look how could we implement a stateful Lua table iterator since we often iterate over Lua tables from C++ side, and this operation is slow. This forced me to learn some internals of Lua and how it stores tables. Up to this point, when a map was saved and the Lua state had to be serialised, we used serpent.dump (from serpent library) to convert the variable called global into a string on the Lua side and then take it out and store it within the save.

Since going through Lua tables from the C++ side happened to be quite easy, I have decided to implement, for an experiment - a native Lua serialiser. This allowed us to completely skip using serpent.dump and instead save them directly. My primary goal was to reduce the loading time since in the old format the saved data was a string that Lua had to parse and execute.

As was noticed later (not by me), the save speed improved a lot due to fact that no Lua operations are executed during save, just pure traversal over data to save in a linear time.

For measurements I was using one save file that has quite a lot of script data in it (script.dat is around 60MB), the result is the average over 3 test runs.

Saving:

Old = 285.429s
New = 2.847s

Loading:

Old = 47.034s
New = 22.755s

This values also includes some optimisations implemented by Rseding.

Changing the serialiser however has its costs. serpent.dump was doing serialisation of Lua functions stored in globals. They were officially unsupported by us anyway but some mods were using them "since they seem to work". With the new serialiser I have decided to not implement it at all due to its complexity and inherent limitations (closures were broken anyway). This broke some mods (even some base game scenarios) but it is rather easy to fix.

An additional consideration we had was if it should fail to save when there are Lua functions in global during save, or should it silently delete them (as happens with metatables). The first approach was considered to be the best to quickly catch all non conforming mods but later we have decided that deleting them on save (and providing some info into log file) was better because a migration was almost impossible due to a base game migration for 0.18.28 that requests to reload all script, that as a side effect: saves the Lua state which would abort saving immediately due to Lua functions still in globals.

                [Discuss on our forums](https://forums.factorio.com/85415)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/gssm2i/friday_facts_349_the_10_plan/)
