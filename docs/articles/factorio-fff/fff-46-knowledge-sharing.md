# Friday Facts #46 - Knowledge sharing

- Источник: https://factorio.com/blog/post/fff-46
- Авторы: Tomas
- Дата: 2014-08-08
- Темы: процесс, команда
- Применимость к проекту: низкая
- Что извлечь: Обмен знаниями внутри небольшой команды: каждый программист должен понимать чужую подсистему настолько, чтобы её чинить.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hi all,

despite the ongoing vacation period there has been 4 of us meeting at the office for the past two days. I returned from the prolonged yoga weekend, Kovarex came back from the paragliding course (yes he will be getting his own paraglide with Factorio logo on it, I saw the proposals :D) and Kuba has finished moving and equipping his new flat. As mentioned in the last edition, the 4th person is a new Factorio developer called Mira who started this Monday.

### Knowledge sharing

We have used the time before Mira's desktop computer arrives (which is taking waaaay longer than it should) to do some Factorio code knowledge sharing. Basically we wrote down a list of about 15 main topics considering the game engine design and programming areas and we took turns going through them and explaining them to him. Among other things we talked about: IDs and prototypes, Logistic Network, AI, Scripts architecture, Multiplayer, Map serialization etc. We decided that proper one-on-one introduction to the code architecture with practical examples is well worth our time and energy. The week has shown that the code is quite big and complex already, therefore we want to make the new developer getting into it process as easy as possible. Near future will show how well this will pay off ...

### Multiplayer progress

Two days ago we had a first "coop game" in Factorio ever. Basically I created a tiny little map and Kuba joined me and we were playing together for like 2 minutes before the game crashed on an assert :D. Most of the "game" was running around, chopping trees, placing building or interacting with them (and also kind of unfair me shooting at him - since I had the gun and he didn't :) ). We will have some video footage for you soon to spice up the reports a bit. So we are slowly making progress. The game is loaded, players can connect / disconnect, they download the map from the others, they can do stuff together in the game, all working as expected.

 Big annoyance at the moment, which prevents us from "playing more", is very frequent waiting for the other peer (which results in pausing the game for fraction of a second). In theory this should happen only occasionally due to the network lag, but now it happens pretty much all the time. Kuba is now working on a small multiplayer log analyzer tool to help us understand better what is causing these lags. Another headache is that the multiplayer crashes when using separate threads for update and render. We will have to rewrite our game loop to smoothly handle cases when for instance one player maximizes his window - the game update should run forward normally (not waiting for display flip) so the other players won't even notice the resulting 1-2 second lag. Also the "rock solid" determinism item still remains on the TODO list and it will be up to Kovarex to dive into this one once stable 0.10.x is out and he has more time.

### 3d department

We haven't had any message from Albert for more than a week. I hope it means he is having a well deserved rest somewhere in the middle of Mediterranean. The last thing he told us is that he is going to participate in a tough sailing race around Mallorca with a crazy Russian ship captain :)

Pavel has been working on more proposals for the tank. A big discussion emerged at our forums after showing some first concepts we were considering. This clearly shows that the tank will be a significant addition to the game if done right. Which we really want to do.

Below is the photo of kovarex failing to take off with his paraglide before a wild bush.

![](https://cdn.factorio.com/assets/img/blog/fff-46-kovarex-paraglide.jpg)

If there is something you would like to say please do so [at our forums](https://forums.factorio.com/forum/viewtopic.php?f=38&t=5262).
