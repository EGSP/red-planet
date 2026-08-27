# Friday Facts #265 - Nomenclature & Steam networking

- Источник: https://factorio.com/blog/post/fff-265
- Авторы: Abregado, Rseding
- Дата: 2018-10-19
- Темы: терминология, процесс, сеть
- Применимость к проекту: средняя
- Что извлечь: Единый словарь понятий проекта: расплывчатое название приводит к расхождению в понимании механики. Отдельно — переход на сетевой слой Steam.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## Factorio Nomenclature Abregado

Today I want to discuss some common problems that we see in video games.

**Inconsistent Terminology**

When I asked out loud "So what is an Intermediate Product anyway?", I got a similar reaction as when someone mentions The [Berlin Interpretation](http://www.roguebasin.com/index.php?title=Berlin_Interpretation) at a rougelike convention. So what is an Intermediate Product? Well it is a product that is used only as an ingredient for something else. No, that's not right because Science Packs are not used in any recipe. So what then, Intermediate products are just things that you can use Productivity Modules on? Perhaps they are simply items that can be found in the Intermediate Crafting menu. Then are they not Intermediate Recipes?

To give another example, answer these questions:

Name the action a player performs when they add an entity to the world?
Name the action a player performs when they remove an entity from the world?
Name the action a player performs when they add a ghost entity to the world?
Name the action a robot performs when they add an entity to the world?
Name the action a robot performs when they remove an entity from the world?

Here are a few situations where the game displays your possible answers:

![](https://cdn.factorio.com/assets/img/blog/fff-265-player-builds.png)

A player *builds*.

![](https://cdn.factorio.com/assets/img/blog/fff-265-player-mines.png)

A player *mines* entities.

![](https://cdn.factorio.com/assets/img/blog/fff-265-robot-build.png)

Robots *repair* and *build* entities, but wait… the player *places* buildings and *builds* ghosts?

![](https://cdn.factorio.com/assets/img/blog/fff-265-robot-construct.png)

But here Robots are *constructing* machines.

![](https://cdn.factorio.com/assets/img/blog/fff-265-robot-deconstruct.png)

Here the robots are *deconstructing* items!

This leads into a discussion about what is an item and what are entities, and that discussion leads us into the next point...

**Internal nomenclature leaking out**

During game development it is very common to use internal names to refer to mechanics, items, or characters. It does not feel like such a big deal, and many early access games simply ignore the problem completely. I'm not going to point any fingers, but if you look you will find some examples. Oh wait, here is some from your favourite early access game!

![](https://cdn.factorio.com/assets/img/blog/fff-265-entity.png)

Internally, things that exist on a surface in the game are called *entities*.

![](https://cdn.factorio.com/assets/img/blog/fff-265-capsules.png)

All these items are capsules internally, but only 5 of them are actually labelled as capsules.

Really, these should be categorised by how players use them, and indeed there is an attempt to do so. Remotes are items used to trigger an effect, Grenades are things you throw... but why is the Poison Capsule not called a gas grenade?

There are more inconsistencies but to keep this article reasonably not-short, I will let you find the others yourself (and to save something for a future FFF about Tooltips).

**Why change?**

You might be thinking that this is not a big problem. Some others might be thinking that the problem is too pervasive to bother changing. There are a few reasons why it is important, the first, and most important of which is our quality mindset; everyone on the team here wants the game to be as great as possible.

Next we should see this increase the quality of the translations. A translation is only as good as its source, and having a consistent usage of words can go a long way to helping the translators do better work. The effect of this can be increased by providing a dictionary of important words to the translators so they can be sure to always use the same term in all places.

Since we are also working on a guided experience (Campaign), this would also help us give much clearer instructions to the player. An example of confusion here would be if one quest said "Place a chest" and another said "Place the item in the chest". The player needs to read the entire quest caption (probably twice), and can never build up a mental map of our language. This leads to the player spending more mental energy (cognitive load) while playing the game. Changing this to "Build a chest" and being consistent, allows the player to create mental shortcuts, meaning the quest tasks require less effort to understand.

Finally, consistency in terminology will help new players, and I don't just mean sub-1 hour playtime players. Factorio is a 'Big Game' and players are encountering new items, entities, concepts, and text for a long time. How many hours did you play before you discovered [this helpful trick](https://www.reddit.com/r/factorio/comments/90y0o3/til_right_clicking_an_assembler_and_left_clicking/), or [this one](https://www.reddit.com/r/factorio/comments/9ht0op/til_you_can_use_num_and_num_to_place_more_or_less/)?

**How to change?**

We could make the vocabulary consistent with what the current player base uses. This option sounds pretty good until I started asking people questions similar to those I asked you at the beginning of the article. Here are another two as a refresher:

Where do biters come from?
I come in 7 colors, what am I?

The only wrong answer is if you said there was only a single right answer.

Prepare your rotten tomatoes, Ben is about to say something unpopular. The influx of players that are to be expected from 1.0 give us an interesting option. We could theoretically change the vocabulary of the game to be more consistent, reasonable, and generally more helpful to players. Then, as new players join the community, this new language will slowly replace the old.

This would help ease communication between all players; veterans and new addicts alike. Consistency will also help polish the experience to the level that players expect from the game.

**Who should change it?**

Before Rseding jumps in with some awesome news, I would ask you to have your say in this [Google form](https://docs.google.com/forms/d/e/1FAIpQLSd__YRCGPEBH5lKUt5MBpue21gtVfA4LDxuWGjUUA3MEYvHZg/viewform). It will be fun to see what you come up with, and I will publish the results in a few weeks.

## Steam Networking Rseding

As many of you might know if you've tried hosting a stand-alone multiplayer game (Factorio or otherwise) from a home internet connection, it's not a very reliable experience. This can be due to multiple different things (bad home routers, company/school firewalls and so on).

I arrived in Prague earlier this week and was given the task of improving that experience through Steam Networking support. The way Steam Networking works is: when you start a multiplayer game, the game can tell Steam that you want to accept connections from other Steam users. The benefit of doing this is if someone can connect to Steam, they can connect to you. Even if your local connection isn't setup to let the multiplayer game work, Steam can route the network data through it's existing connection and get the packets where they need to go.

After a few days of working on it and testing it seems to be working and working reliably. You can just click "start multiplayer game", and anyone on your friends list can click join. You can mix non-steam and steam players on the same server and it "just works". As a bonus while working on this I discovered some issues with the non-steam networking logic that should improve the "Could not establish network communication with server" error that keeps coming up.

As always, let us know what you think on our [forum](https://forums.factorio.com/63028).
