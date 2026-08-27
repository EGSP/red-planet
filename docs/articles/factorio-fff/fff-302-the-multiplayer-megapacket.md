# Friday Facts #302 - The multiplayer megapacket

- Источник: https://factorio.com/blog/post/fff-302
- Авторы: Twinsen, Klonan
- Дата: 2019-07-05
- Темы: сеть
- Применимость к проекту: низкая
- Что извлечь: Отказ восстановления соединения при большом числе игроков: разбор поведения механизма повторной отправки под нагрузкой.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## The multiplayer megapacket Twinsen

Last month I joined [KatherineOfSky's MMO event](https://youtu.be/d_DlvkTK9hU) as a player. I noticed that after we reached a certain number of players, every few minutes a bunch of them got dropped. Luckily for you (but unluckily for me), I was one of the players who got disconnected **every**, **single**. **time**, even though I had a decent connection. So I took the matter personally and started looking into the problem. After 3 weeks of debugging, testing and fixing, the issue is finally fixed, but the journey there was not that easy.

Multiplayer issues are very hard to track down. Usually they only happen under very specific network conditions, in very specific game conditions (in this case having more than 200 players). Even when you can reproduce the issue it's impossible to properly debug, since placing a breakpoint stops the game, messes up the timers and usually times out the connection. But through some perseverance and thanks to an awesome tool called [clumsy](https://github.com/jagt/clumsy), I managed to figure out what was happening.

The short version is: Because of a bug and an incomplete implementation of the latency state simulation, a client would sometimes end up in a situation where it would send a network package of about 400 entity selection input actions in one tick (what we called 'the megapacket'). The server then not only has to correctly receive those input actions but also send them to everyone else. That quickly becomes a problem when you have 200 clients. It quickly saturates the server upload, causes packet loss and causes a cascade of re-requested packets. Delayed input actions then cause more clients to send megapackets, cascading even further. The lucky clients manage to recover, the others end up being dropped.

The issue was quite fundamental and took 2 weeks [to fix](https://cdn.factorio.com/assets/img/blog/fff-302-megapacket-commit.jpg). It's quite technical so I'll explain in juicy technical details below. But what you need to know is that since Version 0.17.54 released yesterday, multiplayer will be more stable and latency hiding will be much less glitchy (less rubber banding and teleporting) when experiencing temporary connection problems. I also changed how latency hiding is handled in combat, hopefully making it look a bit smoother.

## The multiplayer megapacket - The technical part Twinsen

The basic way that our multiplayer works is that all clients simulate the game state and they only receive and send the player input (called *Input Actions*). The server's main responsibility is proxying *Input Actions* and making sure all clients execute the same actions in the same tick. More details in [FFF-149](https://www.factorio.com/blog/post/fff-149)

Since the server needs to arbitrate when actions are executed, a player action moves something like this: Player action -> Game Client -> Network -> Server -> Network-> Game client. This means every player action is only executed once it makes a round trip though the network. This would make the game feel really laggy, that's why latency hiding was a mechanism added in the game almost since the introduction of multiplayer. Latency hiding works by simulating the player input, without considering the actions of other players and without considering the server's arbitrage.

![](https://cdn.factorio.com/assets/img/blog/fff-302-latency-state.png)

In Factorio we have the *Game State*, this is the full state of the map, player, entitites, everything. It's simulated deterministically on all clients based on the actions received from the server. This is sacred and if it's ever different from the server or any other client, a desync occurs.

On top of the *Game State* we have the *Latency State*. This contains a small subset of the main state. *Latency State* is not sacred and it just represents how we think the game state will look like in the future based on the *Input Actions* the player performed.

To do that, we keep a copy of the *Input Actions* we make, in a latency queue.

![](https://cdn.factorio.com/assets/img/blog/fff-302-latency-queue.png)

So in the end the process, on the client side, looks something like this:

- Apply all the *Input Actions* of all players to the *Game State*, as received from the server.

- Delete all the *Input Actions* from the latency queue that were applied to the *Game State*, according to the server.

- Delete the *Latency State* and reset it to look the same as *Game State*.

- Apply all the actions in the latency queue to the *Latency State*.

- Render the game to the player, based on the information from the *Game State* and *Latency State*.

This is repeated every tick.

Getting complicated? Hold on to your pants. To account for the unreliable nature of internet connections, we have two mechanisms:

- Skipped ticks: When the server decides what *Input Actions* will be executed in what game tick, if it does not have the *Input Actions* of a certain player (e.g. because of a lag spike), it will not wait, but instead tell that client "I did not include your *Input Actions*, I will try to include them in the next tick". This is so when a client has connection problems (or computer problems), they will not slow down the map update for everyone. Note that *Input Actions* are never ignored, they are only delayed.

- Roundtrip Latency: The server tries to guess what's the roundtrip delay between the client and the server, for each client. Every 5 seconds it will negotiate a new latency with the client, if necessary, based on how the connection behaved in the past and the roundtrip latency will be increased and decreased accordingly.

By themselves they are pretty straightforward, but when they happen together (which is common when experiencing connection issues), the code logic starts becoming unwieldy, with a large amount of edge cases. Furthermore, the server and the latency queue need to properly inject a special *Input Action* called *StopMovementInTheNextTick* when the above mechanisms come into play. This prevents your character from running by himself (e.g. in front of a train) while experiencing connection problems.

Now it's time to explain how our entity selection works. One of the *Input Action* types we send is entity selection change, which tells everyone what entity each player has their mouse over. As you can imagine this is by far the most common input action sent by the clients, so it was optimized to use as little space as possible, to save bandwidth. The way this was done is that each entity selection, instead of saving absolute, high precision map coordinates, it saves a low precision relative offset to the previous selection. This works well, since a selection is usually very close to the previous selection. This creates 2 important requirements: *Input Actions* can never be skipped and the need to be executed in the correct order. These requirements are met for the *Game State*. But, since the purpose of the *Latency state* is to "look good enough" for the player, these requirements were not met. The *Latency State* didn't account for [many edge cases](https://cdn.factorio.com/assets/img/blog/fff-302-megapacket-commit.jpg) related to tick skipping and roundtrip latency changing.

So you can probably see where this is going. Finally the issue of the megapacket started to show. The final problem was that the entity selection logic relied on the *Latency State* to decide if it should send a selection changed action, but the *Latency State* was sometimes not holding correct information. So, the megapacket got generated something like this:

- Player has connection issues.

- Skipped ticks and Roundtrip Latency adjustment mechanisms start to kick in.

- Latency state queue doesn't account for these mechanisms. This leads to some action being deleted prematurely or executed in the wrong order, leading to an incorrect *Latency State*.

- Player recovers from his connection issue and simulates up to 400 ticks in order to catch up to the server again.

- For every tick, a new entity selection change action is generated and prepared to be sent to the server.

- Client sends the server a megapacket with 400+ entity selection changes (and other actions also. Shooting state, walking state, etc also suffered from this problem).

- Server receives 400 input actions. Since it's not allowed to skip any input action, it tells all clients to execute those actions and sends them over the network.

Ironically, the mechanism that was supposed to save some network bandwidth ended up creating massive network packets.

In this end this was solved by fixing all the edge cases of updating and maintaining the latency queue. While this took quite some time, in the end it was probably worth doing a proper fix instead of some quick hacks.

## Clusterio - The Gridlock Cluster Klonan

Clusterio is a scenario/server system that adds communication of game data between different servers. For instance sending items between different servers, so you can have a 'mining server', which will mine all the iron ore you need, and send it to the 'smelter server' which will smelt it all and pass it further along.

Last year there was an event using the system which linked [over 30](https://cdn.factorio.com/assets/img/blog/fff-302-old-clusterio-cloud.png) servers together to reach a combined goal of 60,000 Science per minute. [MangledPork](https://www.youtube.com/channel/UCwTLrdvrscYPXBZ3Z0kzX0g) has a video of the start of last years event [on YouTube](https://youtu.be/R2TKzW_oEIU).

The great minds and communities behind the last event have come together again to host another Clusterio event: [The Gridlock Cluster](https://www.reddit.com/r/factorio/comments/c98wui/the_gridlock_cluster_a_clusterio_based_event/). The goal this time is to push the limits even further, explore and colonise new nodes as they are generated, and enjoy the challenge of building a mega-factory across multiple servers.

If you are interested in participating in this community event, all the details are listed in the [Reddit post](https://www.reddit.com/r/factorio/comments/c98wui/the_gridlock_cluster_a_clusterio_based_event/), and you can join the Gridlock Cluster [Discord server](https://discordapp.com/invite/Ugs4AAM).

As always, let us know what you think on our [forum](https://forums.factorio.com/72928).

                [Discuss on our forums](https://forums.factorio.com/72928)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/c9fwie/friday_facts_302_the_multiplayer_megapacket/)
