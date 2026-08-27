# Friday Facts #60 - Tests all around

- Источник: https://factorio.com/blog/post/fff-60
- Авторы: Tomas
- Дата: 2014-11-14
- Темы: тестирование, инструменты, процесс
- Применимость к проекту: высокая
- Что извлечь: Уровни автоматических проверок: модульные тесты, сценарные прогоны и тесты производительности; описано, что именно каждый уровень обнаруживает.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hey guys,

  here comes the weekly dose of Factorio-world information for you. We have crafted it just before we leave for a [gdsession](http://gdsession.com/) warm-up party. Gdsession is quite a big game development conference here in Prague. This is the first year we are going to participate. And actually tonight we will have a short (10 minutes) talk about what we do (well, about Factorio obviously).

### Release on Thursday

Long long time ago, we used to have releases on Thursday. It is actually so long ago, that only the most hard-core fans will remember that. We have abandoned this for a simple reason. There was a Thursday and we were still coding like crazy. So we postponed the release to Friday back then. Now we would like to get back to "release on Thursday" scheme. The reasons are clear:

- There is much less stress on Thursday than on Friday (when fans are ready for the weekend and "demanding" the release:)).

- We are people. Friday is the day to spend evenings with friends (and not fixing last minute bugs to ship the release :)).

- If something goes wrong we can make a hotfix the next day and still ship the release for the weekend.

So yesterday we started a new tradition of releasing on Thursday instead of on Friday (of course it doesn't mean every Thursday). This was done by coming out with the [0.11.2](https://forums.factorio.com/forum/viewtopic.php?f=3&t=6661). Full of bug fixes, couple of cosmetic multiplayer improvements (last IP address of server is remembered) and sweet new graphics for the logistic bots. The third point in the above list has already shown to be valid because today we have made a [0.11.3](https://forums.factorio.com/forum/viewtopic.php?f=3&t=6683) hotfix which should fix a few annoying and game preventing bugs (looking at you "manual train building crash").

### Automated testing

Almost a year ago we [first mentioned](https://forums.factorio.com/forum/viewtopic.php?f=38&t=1911) that we would start with automated testing. Since then the topic of automated testing came up [couple](https://www.factorio.com//blog/post/fff-29) of [times](https://www.factorio.com//blog/post/fff-53). For a long time our testing suite was just ridiculous (meaning not doing much). But that has changed recently. We have spent quite some time writing tests in the past weeks and working on our testing framework. And that effoert is starting to pay off. We are slowly getting into (very much beneficial) mindset of writing tests for any new functionality we add to the game. We believe, this is something that will pay off in the long run (especially taking into account the complexity of the game - and the code behind - that is there already). At the moment we have two growing sets of tests.

**Unit tests** are aimed at testing non-trivial internal functions. They work with mocked objects and the result is that they give us a certain assurance that the specific part of the code functions correctly on itself. They are really fast to run, this is because they don't load any graphics nor any prototypes (data from the base mod). Examples of unit tests are testing synchronization mechanisms in the multiplayer code, tests in the crafting queue logic or even testing that floating point operations give the same result on all our architectures.

**Integration tests** on the other hand test the game as a whole (with data present in the base mod). The standard pattern here is that every test creates a small map, places couple of objects on the map, runs updates and then verifies that expected conditions are met. For instance the test can create a tricky belt setup, place some items at the belts and then check whether after some updates, the items are blocked on belts as expected. Integration tests are really helpful for determinism regression testing. After every test (and sometimes in the process of it as well) a crc check is made against the set of presaved crc values (these are simply regenerated when the game logic changes).. This means that if a bug is introduced that would break determinism (giving different results under different circumstances or on different operating systems) in code covered by tests, we will find out just by running the test suite. And that is something we plan to do automatically when building the game (the build server configuration is in progress:)).

One more test suite is slowly shaping up in our minds. This one doesn't have the name yet, but it is aimed at testing the game completely from outside (could be called "**black box**" testing - because Factorio binary is treated as a black box from the outside). This is necessary to test some multiplayer edge cases and useful for testing gui interaction. For instance in the 0.11.x there is a known issue of game desync / crash when multiple players connect to the game at the same time. The "black box" test here would simply start three instances of the game and then via a control script coordinate the instances to join the multiplayer game at the same time. Not only would this be a reproducible way of testing such a case, but it would be actually useful in figuring out what the problem is. At the moment if I run three instances of the game at my computer the system gets really laggy and it is hard to do any debugging (that is why the mentioned bug is not yet fixed). As a bonus the mechanism for "black box" testing could be used for running "headless" (no graphics) servers for multiplayer (a feature quite a few people on the forums asked for).

So in short we are going in the automated testing direction now. And we are actually having fun doing so. That doesn't mean that there will be no bugs from now on:) But it should help us avoid same bugs in the future and give us more confidence for refactorings.

### Player running animation

The 0.11 has brought a new player model. In general we are really happy with it, however the running animation has been a big PITA from the beginning. We have failed to get it just right. And the players noticed. The thing is that it is not that simple. Albert has done many tests, studied the movement of human bodies and watched videos of professional runners to come out with good looking animation. If you came to see us in our office sometimes this week there is a good chance we would be running around in the "graphics department room" trying to analyze the natural human movement while running:) Anyway we are getting closer to a working version, so this will be fixed during the 0.11.x stabilization process. In the meantime you can have a look at the history of the running animation shown in gifs below.

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-00.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-01.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-02.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-03.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-04.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-05.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-06.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-07.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-08.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-09.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-10.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-11.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-12.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-13.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-14.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-60-player-walk-15.gif)

If you have anything to say, [the thread](https://forums.factorio.com/forum/viewtopic.php?f=38&t=6682) at the forum is there for you.
