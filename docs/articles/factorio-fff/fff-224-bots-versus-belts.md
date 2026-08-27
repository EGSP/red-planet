# Friday Facts #224 - Bots versus belts

- Источник: https://factorio.com/blog/post/fff-224
- Авторы: Twinsen, Kovarex
- Дата: 2018-01-05
- Темы: геймдизайн, баланс
- Применимость к проекту: высокая
- Что извлечь: Логистические боты обесценивают конвейеры: разбор случая, когда одна механика делает другую ненужной, и обсуждение вариантов исправления.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
### The 0.16 stabilisation update (kovarex)

We had quite a lot of critical bugs after the 0.16.8 release that introduced the logistic chest finalisations mentioned in the [last FFF](https://www.factorio.com/blog/post/fff-223). I'm sorry for the trouble, but it is called experimental version for a reason.

It seems that 7 releases in the past week has been enough to stabilize it. We are finally in a state, where we are fixing more bugs than are reported, and we are reaching the first boundary of less than 100 active bug reports.

It seems that the most urgent things are to be be finished soon, and we could find the needed time to dive into the belt logic to be able to consolidate it. This is my plan for the next week.

### Removing logistics bots from the game (Twinsen)

During more boring FFF, I like to do these gameplay rants where I share my ideas about game design. Here is one of them.

Ever since I started playing Factorio, I have thought of logistic bots as too powerful. Actually, I refused to use them thinking "surely there is some catch to this and they can't replace belts". Since then I was always a "bot hater". I believed it trivialized base building and managing belts. I also believe that building belts is way more fun due to it's inherent complexities, challenges, and emergent situations (the most common example being belt balancing).

Bots vs. belts is a controversial subject, even in our team, but most of us believe bots are too powerful. So we did small nerfs from time to time in an effort to compensate for this, but we still keep coming to the same conclusion.

My argument is that bots are simply fundamentally better. Bots basically cheat by "teleporting" items, plus they are extremely easy to build and expand. This means that almost any nerf we throw at them is always solved by building more bots and/or more roboports and/or more solar panels. All of this can be done with a simple copy paste using blueprints. Plus they are even UPS friendly, so it seems like bots are the win-win-win solution.

So I had this rather crazy idea of removing logistic bots from the game. Basically my idea was that construction bots would still be a thing and player logistics would still work. This would be done by:

- Merging logistic and construction bots into just Flying Robots.

- Removing Active provider, Buffer and Requester chests and having just Passive provider and Storage chests.

- Flying Robots will do everything construction bots do now and also supply the player.

- Simplifying recipe complexity a bit so the belt spaghetti does not get ridiculous.

Now, hopefully you aren't smashing your desk and writing us an angry email. Don't worry, logistics bots won't be removed from the game, mostly because it's a feature that has been developed and polished quite a lot. Also many players love the feature, and we've all become used to it over the years. But think of it a different way, imagine there were two parallel universes:

- The universe we are in, where Factorio has had logistic bots since very early.

- A universe where Factorio never had this feature. Construction bots were eventually added, and using bots to move items freely is nothing more than an idea that pops up from time to time but it's quickly discarded due to it breaking the game.

Which Factorio would be the best and most fun one for the players in that universe?

To be honest it's very hard to decide. I would go with the more pure Factorio from universe 2 that focuses on it's core and most fun mechanic: belts and belt logistics. I'm curious what you guys think.

I mentioned this, because I think this way often in an attempt to "make the best game ever" without being influenced by my biases of being used to some feature or style of play.

But let's return to reality and the game we have now. Quite recently I actually realized that bots are not as bad as I thought. The logistics system is placed very late in the tech tree, so most of a typical playthrough will be done using belts. The fact that you get this almost game breaking feature is not so bad because it's late in the game. After this you can continue to challenge yourself and build even bigger using bots, or belts or both.

We are still looking to incentivize belt building a bit more, since it is the more fun way to play Factorio, but the question is how.

We have ideas like increasing the power consumption, decreasing the maximum stack size bots can carry to 2 items or buffing belts by adding a "stacked" belt tier.

Like I mentioned before, I try to balance things by looking at numbers and objective facts. I'm trying to determine how much better bots are than belts, so I can know if for example nerfing the stack size to 2 will have the desired effect, or just make players angry. I thought of metrics like throughput over time to set up, or throughput over base size.

But how do these metrics influence the big picture and will any of these make the game more fun in the long run?

We don't know the answer to these questions yet. What is your take on this bots versus belts thing? What changes, if any, could we do to make everything more fun?

Pro-bot arguments:

- The fact that they are so powerful, gives a very big sense of progression. They are behind a late game research, and gives you things that are very powerful and game changing.

- It adds large diversity to the game.

Anti-bot arguments:

- Bot bases are usually less complex and less interesting to look at, manage and expand.

- Because of their ease of use (and apparent ease of use), players tend to go to bots. We believe that belts are more fun, but we are guiding the player towards a less fun style of play.

- When you learn that bots exist and how they work, building bases with belts just seems tedious.

Related to this I'd like to give a shout-out to forum member ultramn, for his interesting [Self-contained 1,000 SPM base](https://forums.factorio.com/56054). We analysed his save while we were having some bot vs. belt discussions this week.

His save proves that bots can get ridiculously powerful, but at the same time it shows that there is a lot of fun to be had with bots if you challenge yourself to make something.

[![](https://cdn.factorio.com/assets/img/blog/fff-224-bots-small.jpg)](https://i.redd.it/ugtf1rydgk701.jpg)

As always, let us know what you think on our [forum](https://forums.factorio.com/56218).
