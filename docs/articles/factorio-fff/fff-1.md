# Friday Facts #1

- Источник: https://factorio.com/blog/post/fff-1
- Авторы: Tomas
- Дата: 2013-09-27
- Темы: геймдизайн, ИИ противника, процесс
- Применимость к проекту: средняя
- Что извлечь: Первая запись серии. Управление противником вынесено из скриптов Lua в код игры, а агрессия привязана к загрязнению: источник угрозы вычисляется из состояния мира, а не задаётся сценарием.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello,

 recently we had a lot of discussions on our forum about interaction with the community. You can have a look at the [details](https://forums.factorio.com/forum/viewtopic.php?f=5&t=1239).
  The result is that we have realized, we have done a poor job in providing updates about our progress. And we have decided to change it.

 This is a first blog post from the **"Factorio Friday Facts"** series. Every friday we will sum up our weekly progress here. This will include insights into what we have been working on, what is planned, sometimes art sneak peeks
  of course occasional fun facts from Indie game developers live.

 All right, let's have a look at what we have been up to in the past week. The single point of focus for the whole team has been finishing the 0.7.0 release. This release has taken the most development time so far. Partly because
  we were getting our attention together after the holidays and partly because there was a LOT of work to be done:) Actually most of the changes are under the hood (combat framework, AI routines, pollution modelling, etc.) but
  their results will be well visible in the game.

 Just a taste of what is coming. The biggest change is probably the overall shift in enemy logic. Before they were simply controlled by the lua script behind the scenario. Now they are more autonomous and integrated in the game.
  This is because of a new concept we have introduced - the pollution. Pollution is produced by the factory (mining drills, furnaces, etc.) and then spreads in the world. Some objects can actually lower pollution (for now only trees).
  After a while the deadly pollution clouds arrive to the enemy spawners. The spawners "clear" the pollution but in turn produce an "angry" biters (new name for our basic class of enemies). After certain period of time, all the
  angry biters from the local neighborhood are sent together to the closest local optimal pollution source to destroy everything that they find there. This follows a simple logic: you destroy their environment - they get angry - they attack you / your machines. For now there is no way to deal with the pollution in the "peaceful" way, but there will be in the future. You can imagine pollution clearing machines or even making pacts with enemies to tolerate
  pollution in exchange for providing them with supply of resources. Anyway this changes the game significantly. It is not enough to fortify the labs / radars now. You need to protect your whole factory / keep checking the pollution levels and examine from which direction the attack will come (the pollution is visible on the map after pressing the alt key).

 For Albert (our graphic) the release has been especially challenging because after months of working on machine design he had to completely shift his style and model the enemy units and structures.
  The result are three new biters and three worms taking up a lot of MBs in the package:) Last week Albert spent with the most difficult structure - the enemy spawner. In the end we didn't put it into the release, because
  it still needs to be tweaked and better integrated, but it is definitely coming in one of the following bugfix releases. Apart from that Albert also spent some time preparing a new set of selection boxes and arrows.
  He was correct that the old ones "were hurting the eyes". You can check an example of new boxes in the screenshot below (personally I am really fond of them, that is why I share them here).

![](https://cdn.factorio.com/assets/img/blog/fff-1-huds-1.jpg)

![](https://cdn.factorio.com/assets/img/blog/fff-1-huds-2.jpg)

![](https://cdn.factorio.com/assets/img/blog/fff-1-huds-3.jpg)

 Since the release is rather big we have spent the last week mostly by fixing bugs and solving small remaining issues. AI behaviors are especially tricky to test because they are often not exact or easily reproducible. The final count of solved issues in the 0.7.0 has stopped at symbolic 111. Couple of them done at the very last moment:) After this sprint we will now take some rest over the weekend and start preparing the plans for the next update.
      Next month will be especially interesting because we will start actively preparing for the Steam greenlight campaign. This will require further graphical polishing in the game (namely player animation) and most importantly a new trailer. We already have couple of ideas and for sure we will discuss them with the community on the forums.

 And finally the fun fact. During release compilation on our Linux virtual machine we got an error: "Virtual memory exhaust, cannot allocate more memory". Factorio code is getting really big. So there is no Linux build
  until we fix this:|

 Well that would be it for now. The 0.7.0 release is actually out already, you can read more details on our [forum](https://forums.factorio.com/forum/viewtopic.php?f=3&t=1295). It is still experimental and it will probably take some time before it stabilizes, but if you don't mind the bugs then go ahead and give it a spin.
