# Friday Facts #143 - Matching server and UDP NAT punching

- Источник: https://factorio.com/blog/post/fff-143
- Авторы: cube
- Дата: 2016-06-17
- Темы: сеть
- Применимость к проекту: низкая
- Что извлечь: Сервер сопоставления и пробивание NAT через UDP: разбор того, почему прямое соединение между клиентами часто невозможно.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello all,

### About 0.13.0

The hype for the upcoming 0.13.0 release has been growing. There are two news I can give you at the moment. As usual one is "bad" and the other is "good". So let's start with the bad one. The release has been postponed. Now the good one. The release has a fixed and specific date now (as opposed to "yeah soonish ..."). The date is 27th of June (a week from Monday). This is a hard deadline. Basically unless something very unexpected happens (i.e. the office being hit by the meteor and our release server not surviving the impact=)) that is when the release will be.

There are a few good reasons for us to postpone the release.

- The NAT punching (see the rest of the post) has been just finished and we need to do some testing of it.

- [The mod portal](https://www.factorio.com/blog/post/fff-141) requires some final tweaks to be at least basically functional.

- Issues are still being found during internal playtesting.

- kovarex is away for holidays and in his words "he prefers to relax without worrying about all the bug reports coming up after the release"

Just to share one of the many many issues / details that we came across during the playtesting. Below you can see a debugging visualisation for the smoke animation. Michal (posila) has been using it to figure out if the smoke animation is not getting stuck (which it was) and if the sprites are being drawn in the proper order (z axis wise).

![](https://cdn.factorio.com/assets/img/blog/fff-143-smoke-debugging.jpg)

### Matching server

In [FFF 139](https://www.factorio.com//blog/post/fff-139) we talked about
a matching server that is going to be integrated in 0.13.0.
The matching server will allow you to mark your multiplayer game as public and
announce it to other players, you know how it usually works.

Tomas implemented the server and it's counterpart in Factorio itself, but we
didn't get around to testing it as a whole for a long time.
Eventually (while still not testing the feature), we figured that the server will
not work for us here at the office, because we are hopelessly behind NAT (and with
UPnP disabled, before you even ask).
This realisation made us move the "NAT punching" card from "Ideas for 0.14" to "Priority".

### NAT punching and how it works

When a client wants to connect to a server, the server needs to somehow announce
its IP address and port for the client to connect to.
For a web server this is done using DNS (and pre-determined port numbers), for
Factorio multiplayer the matching server has the same role.

With the server behind NAT there are two problems.

First there is no way for the server to determine its IP address and port without
external help and the IP address and port might not be the same for everyone on
the internet.

To solve this we have yet another two servers ([called pingpong1 and pingpong2](https://twitter.com/codinghorror/status/506010907021828096)!).
When a server starts it asks both of them "What is my IP address?".
If we get an answer from both and the answers are the same, it means that the address
received should work for all other clients on the internet.
If we receive only one of the answers or if the answers differ, then something is
wrong and you're out of luck :-)

![](https://cdn.factorio.com/assets/img/blog/fff-143-get-own-ip.png)

The second problem is that most NATs in use will not allow an incoming packet
unless there have already been some data sent to the address and port that originated
the packet. After some time

We avoid this problem by keeping a connection open between the game server and
pingpong with periodic keepalive packets.
When a client wants to connect to the server through matching server, it first
sends a message to the server through pingpong which makes the game server send
a first packet to the client and open the mapping in its NAT.

![](https://cdn.factorio.com/assets/img/blog/fff-143-connection-request.png)

The behavior we use is actually a specialised version of [STUN](https://en.wikipedia.org/wiki/STUN)
and [ICE](https://en.wikipedia.org/wiki/Interactive_Connectivity_Establishment)
adapted to run over Factorio protocol.
And it looks like it's working :).
Unfortunately it will not work under all circumstances (we expect about 70%), but
better than nothing, right? :-).

The comment thread [at our forums](https://forums.factorio.com/26831) is ready.

Next FFF edition will most probably be written by Albert about recent changes and progress in our graphics department.
