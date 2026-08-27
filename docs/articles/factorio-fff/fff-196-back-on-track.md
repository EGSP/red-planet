# Friday Facts #196 - Back on track

- Источник: https://factorio.com/blog/post/fff-196
- Авторы: Rseding91, Twinsen, Klonan
- Дата: 2017-06-23
- Темы: сеть, инструменты
- Применимость к проекту: низкая
- Что извлечь: Собственный разборщик протокола для Wireshark: свой формат пакетов становится читаемым в стандартном анализаторе.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello, after a lot of planning and preparation, the party on Saturday went very well. We really enjoyed spending time with some of our fans, and it has definitely sharpened our motivation to do right by our community and make the game as great as possible. With this festivity behind us, we started this week with some renewed focus.

### 0.15 Stabilization push Rseding91

These last few days we have made a larger push to handle all the 0.15.x bugs reports on the forum, our current estimate is that we will have a stable release within a few weeks. As the player base has grown so has the number of bugs found - things which we haven't touched for quite some time end up being broken in interesting or weird ways. Most of the time the fixes are simple, but they can have unforeseen consequences that show up quickly with the number of daily active players. We do our best to test that we don't break anything with a bug fix - and write tests when it makes sense.

However sometimes this isn't enough, and we happen to get something like this recent bug introduced in 0.15.22 and fixed in 0.15.23: As of 0.15.22 modded GUI elements are marked per-element with which mod created them so when the mod is no longer active they can be automatically removed. The logic was simple: store a mapping of the mod name to the elements it owns. On load if the mod isn't active or doesn't exist, all the elements that it owned are removed. It was tested through save/load and through mod removal, and the system worked great. Except it didn't. Almost as soon as 0.15.22 was released, someone reported a problem with losing modded GUI elements through save/load.

It turned out that due to how we store mod names in the game ("mod-" + mod-name), some logic wasn't working correctly. If the mod added between 4 and the length of the mods name + 4 GUI elements, it would break and falsely detect the mod as removed when loaded. If the mod added less than 4 or more than the mods name + 4 GUI elements it worked just fine. We just happened to test a mod that only added 1 GUI element so all the testing worked perfectly.

### Wireshark dissector for Factorio - quick and dirty (Technical) Twinsen

A couple of weeks ago I joined a Factorio MMO event from [KatherineOfSky](https://www.youtube.com/channel/UCTIV3KbAvaGEyNjoMoNaGtQ). In the event, all players were invited to join the server. But as we were reaching about 60 players, people were starting to get dropped and they were unable to connect back. It looked exactly like a network bandwidth problem so my first thought was "well, get a better server before hosting an MMO event". But I started looking at the traffic on my computer and there was some unusually high bandwidth being used, especially during connection. Later I was shown that with about 60 online players and no one downloading the map, the server was uploading game traffic at up to 90 MB/s (yes megabytes).

So I started Wireshark, my favorite and I believe the best tool for inspecting network packets. I captured the game traffic and started to look around. I could see that there was a very large amount of packets but since everything was binary data, interpreting the packets was not easy. It took me 10 minutes just to decode a few fields of one packet. It was hard to know what was being sent that is so big. So since I like Wireshark so much, I decided to extend it so it can interpret Factorio packets.

Factorio's network packets are extremely complex. We have 175 InputAction types(e.g. StartWalking, CursorTransfer, EditTrainSchedule, ChangeArithmeticCombinatorParameters), 22 SynchronizerActions (e.g. MapReadyForDownload, ChangeLatency), 17 Network Message types(e.g ConnectionRequest, ServerToClientHeartbeat, MapTransferBlock) all of these each with possibly tens of fields, plus many more intermediate data types that hold all of these together. Add some more logic such as custom packet fragmentation, It was clear that I could not simply write a packet interpreter from scratch,  for example using [Wireshark's Lua api](https://wiki.wireshark.org/Lua). I would have to reuse Factorio's code as much as possible in order to save time.
Part of the team, including me, thought that it might not even be worth spending time on making this tool, especially since in the meantime we found out what was causing the large bandwidth problems. That meant that I would either have to stop or make the tool as quickly as possible.
I choose to try and make the tool as fast as possible. In order to make a C/C++ plug-in for wireshark, I had to install and setup the entire wireshark build environment. Meanwhile Factorio is built using [FASTBuild](http://www.fastbuild.org/docs/home.html). We had to somehow bring these together. The solutions we were thinking of were:

- Build everything in wireshark's build environment. This meant adding only the relevant networking classes from Factorio into Wireshark's project. Some would say this would be the 'proper' way to do it. Unfortunately everything in Factorio is tightly coupled. Some networking classes access entities, or prototypes or graphics or GUI. This means I would have to go through hundreds of classes and manually remove any code and includes that are not related to networking. Not to mention I would then have to make them build correctly in a different build environment. While this would be the 'proper' solution it would take a very long time and it would be hard to maintain.

- Build Wireshark in Factorio's build environment. Wireshark's build is very complex. It has 424 CMakeLists/Makefile files totaling 5086 lines. There would be no way I would make that build correctly in our environment using FASTBuild.

- Build Wireshark with a small interface in it's own environment and have it link to Factorio's library which is built in FASTBuild. This meant that that the Wireshark plugin would include almost the entire Factorio code including gui, graphics, sound, lua, etc. It's not what some people would call proper, but it would work. One day of tweaking compiler flags I never knew existed and I managed to get them to link correctly. From here it's just a matter of creating instances of a few classes to get the networking part of Factorio running and calling some methods from the Factorio code in order to build a tree of the data. In the end I put everything in one DLL. The DLL is a massive 20MB, but it works and it's actually easy to maintain.

I would say that the lesson to take from this is that what looks like a quick and dirty hack might in the end be a much better solution. I ended up with an easy to maintain plugin that gets the job done and I did it in a little over 2 weeks. Here is a screenshot of how inspecting a packet looks like.

[![](https://cdn.factorio.com/assets/img/blog/fff-196-wireshark.png)](https://cdn.factorio.com/assets/img/blog/fff-196-wireshark.png)

So now when you want to report a bug related to networking or inability to connect, adding a Wireshark (.pcapng) capture might help us debug the problem.
Regarding the bandwidth problems, they were caused by the blueprint library when players with very very large blueprint libraries were in game. This has since been fixed by Oxyd, and he is working on improving the synchronization bandwidth further.

### Mini-tutorial review Klonan

I am looking to go over the newly added tutorials of 0.15, and to try see what was done well, and which areas need some improvement. I would like to ask for some community feedback on this topic. At the moment we have the 5 train tutorials, but more will be in the works soon. I don't want to start work on new tutorials until myself and the others in the team are satisfied that we have the process and mechanics of the tutorials working perfectly.

So if you have any comments or feedback on the mini-tutorials, factorio or just something you'd like to say, we welcome you to fill us in over on our [forum](https://forums.factorio.com/50136)
