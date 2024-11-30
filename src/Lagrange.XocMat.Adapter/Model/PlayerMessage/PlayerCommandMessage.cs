﻿using ProtoBuf;

namespace Lagrange.XocMat.Adapter.Model.PlayerMessage;

[ProtoContract]
internal class PlayerCommandMessage : BasePlayerMessage
{
    [ProtoMember(8)] public string Command { get; set; } = "";

    [ProtoMember(9)] public string CommandPrefix { get; set; } = "";

}
