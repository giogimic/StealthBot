Attribute VB_Name = "modCommandsOps"
Option Explicit
'This module will hold all commands that have to deal with holding operator over a channel

Public Enum CacheChanneListEnum
    enRetrieve = 0
    enAdd = 1
    enReset = 255
End Enum


Public Sub OnCancel(Command As clsCommandObj)
    If (VoteDuration > 0) Then
        Command.Respond Voting(BVT_VOTE_END, BVT_VOTE_CANCEL)
    Else
        Command.Respond "No vote in progress."
    End If
End Sub

Public Sub OnChPw(Command As clsCommandObj)
    Dim Delay As Integer
    If (Command.IsValid) Then
        Select Case (LCase$(Command.Argument("SubCommand")))
            Case "on", "set":
                If (LenB(Command.Argument("Value")) > 0) Then
                
                    BotVars.ChannelPassword = Command.Argument("Value")
                    If (BotVars.ChannelPasswordDelay <= 0) Then BotVars.ChannelPasswordDelay = 30
                    
                    Command.Respond StringFormatA("Channel password protection enabled, delay set to {0}.", BotVars.ChannelPasswordDelay)
                Else
                    Command.Respond "Error: You must supply a password."
                End If
            
            Case "off", "clear":
                BotVars.ChannelPassword = vbNullString
                BotVars.ChannelPasswordDelay = 0
                Command.Respond "Channel password protection disabled."
            
            Case "time", "delay", "wait":
                If (StrictIsNumeric(Command.Argument("Value"))) Then
                    Delay = Val(Command.Argument("Value"))
                    If ((Delay < 256) And (Delay > 0)) Then
                        BotVars.ChannelPasswordDelay = CByte(Delay)
                        Command.Respond StringFormatA("Channel password delay set to {0}.", Delay)
                    Else
                        Command.Respond "Error: Invalid channel delay."
                    End If
                Else
                    Command.Respond "Error: Invalid channel delay."
                End If
            
            Case Else:
            
                If ((LenB(BotVars.ChannelPassword) = 0) Or (BotVars.ChannelPasswordDelay = 0)) Then
                    Command.Respond "Channel password protection is disabled."
                Else
                    Command.Respond StringFormatA("Channel password protection is enabled. Password [{0}], Delay [{1}].", _
                        BotVars.ChannelPassword, BotVars.ChannelPasswordDelay)
                End If
        End Select
    End If
End Sub

Public Sub OnClearBanList(Command As clsCommandObj)
    g_Channel.ClearBanlist
    Command.Respond "Banned user list cleared."
End Sub

Public Sub OnD2LevelBan(Command As clsCommandObj)
    Dim Level As Integer
    If (LenB(Command.Argument("Level")) > 0) Then
        Level = Command.Argument("Level")
        If (Level > 0) Then
            If (Level < 256) Then
                Command.Respond StringFormatA("Banning Diablo II users under level {0}.", Level)
                BotVars.BanD2UnderLevel = CByte(Level)
            Else
                Command.Respond "Error: Invalid level specified."
            End If
        Else
            BotVars.BanD2UnderLevel = 0
            Command.Respond "Diablo II level bans disabled."
        End If
        Call WriteINI("Other", "BanD2UnderLevel", BotVars.BanUnderLevel)
    Else
        If (BotVars.BanD2UnderLevel = 0) Then
            Command.Respond "Currently not banning Diablo II users by level."
        Else
            Command.Respond StringFormatA("Currently banning Diablo II users under level {0}.", BotVars.BanD2UnderLevel)
        End If
    End If
End Sub

Public Sub OnDes(Command As clsCommandObj)
    If (g_Channel.Self.IsOperator) Then
        If (LenB(Command.Argument("Username")) > 0) Then
            Call frmChat.AddQ("/designate " & Command.Argument("Username"), PRIORITY.COMMAND_RESPONSE_MESSAGE, Command.Username)
            Command.Respond StringFormatA("I have designated {0}.", Command.Argument("Username"))
        Else
            If (LenB(g_Channel.OperatorHeir) > 0) Then
                Command.Respond StringFormatA("I have designated {0}.", g_Channel.OperatorHeir)
            Else
                Command.Respond "No user has been designated."
            End If
        End If
    Else
        Command.Respond "The bot does not currently have ops."
    End If
End Sub

Public Sub OnExile(Command As clsCommandObj)
    If (Command.IsValid) Then
        Call OnShitAdd(Command)
        Call OnIPBan(Command)
    Else
        Command.Respond "Error: You must specify a username."
    End If
End Sub

Public Sub OnGiveUp(Command As clsCommandObj)
    ' This command will allow a user to designate a specified user using
    ' Battle.net's "designate" command, and will then make the bot resign
    ' its status as a channel moderator.  This command is useful if you are
    ' lazy and you just wish to designate someone as quickly as possible.
    
    Dim I         As Integer
    Dim opsCount  As Integer
    Dim sUsername As String
    Dim colUsers  As New Collection
    
    If (Command.IsValid) Then
        sUsername = Command.Argument("Username")
        If (g_Channel.GetUserIndex(sUsername) > 0) Then
            If (g_Channel.Self.IsOperator) Then
                opsCount = GetOpsCount
                
                If (StrComp(g_Channel.Name, "Clan " & Clan.Name, vbTextCompare) = 0) Then
                    If (g_Clan.Self.Rank >= 4) Then
                        'Lets get a count of Shamans that are in the channel
                        For I = 1 To g_Clan.Shamans.Count
                            If (g_Channel.GetUserIndexEx(g_Clan.Shamans(I).Name) > 0) Then
                                colUsers.Add g_Clan.Shamans(I).Name
                            End If
                        Next I
                        
                        If (opsCount > colUsers.Count) Then
                            Command.Respond "Error: There is currently a channel moderator present that cannot be removed from his or her position."
                            Exit Sub
                        End If
                        
                        'Lets demote the shamans
                        For I = 1 To colUsers.Count
                            g_Clan.Members(g_Clan.GetMemberIndexEx(colUsers.Item(I))).Demote
                        Next I
                        
                        opsCount = GetOpsCount
                    End If
                End If
                
                If (StrComp(Left$(g_Channel.Name, 3), "Op ", vbTextCompare) = 0) Then
                    If (opsCount >= 2) Then
                        Command.Respond "Error: There is currently a channel moderator present that cannot be removed from his or her position."
                        Exit Sub
                    End If
                ElseIf (StrComp(Left$(g_Channel.Name, 5), "Clan ", vbTextCompare) = 0) Then
                    If ((g_Clan.Self.Rank < 4) Or (Not StrComp(g_Channel.Name, "Clan " & Clan.Name, vbTextCompare) = 0)) Then
                        If (opsCount >= 2) Then
                            Command.Respond "Error: There is currently a channel moderator present that cannot be removed from his or her position."
                            Exit Sub
                        End If
                    End If
                End If
                
                Call bnetSend("/designate " & reverseUsername(sUsername))
                Call Pause(2)
                Call bnetSend("/resign")
                
                For I = 1 To colUsers.Count
                    g_Clan.Members(g_Clan.GetUserIndexEx(colUsers.Item(I))).Promote
                Next I
            Else
                Command.Respond "Error: This command requires channel operator status."
            End If
        Else
            Command.Respond "Error: The specified user is not present within the channel."
        End If
    End If
End Sub

Public Sub OnIPBan(Command As clsCommandObj)
    Dim dbAccess As udtGetAccessResponse
    Dim dbTarget As udtGetAccessResponse
    Dim sTarget  As String
    
    If (Command.IsValid) Then
        
        If (Not g_Channel.Self.IsOperator) Then
            Command.Respond "The bot does not currently have ops."
            Exit Sub
        End If
        
        dbAccess = GetCumulativeAccess(BotVars.Username)
        If (Command.IsLocal) Then
            dbAccess.Rank = 201
            dbAccess.Flags = "A"
        End If
        
        sTarget = StripInvalidNameChars(Command.Argument("Username"))
        
        If (LenB(sTarget) > 0) Then
            If (InStr(1, sTarget, "@") > 0) Then sTarget = StripRealm(sTarget)
            
            If (dbAccess.Rank < 101) Then
                If (GetSafelist(sTarget) Or GetSafelist(Command.Argument("Username"))) Then
                    Command.Respond "Error: That user is safelisted."
                    Exit Sub
                End If
            End If
            
            dbTarget = GetCumulativeAccess(Command.Argument("Username"))
            
            If ((dbTarget.Rank >= dbAccess.Rank) Or _
                ((InStr(1, dbTarget.Flags, "A", vbTextCompare) > 0) And (dbAccess.Rank < 101))) Then
                Command.Respond "Error: You do not have enought access to do that."
            Else
                Call frmChat.AddQ(StringFormatA("/ban {0} {1}", Command.Argument("Username"), Command.Argument("Message")), , Command.Username)
                Call frmChat.AddQ(StringFormatA("/squelch {0}", Command.Argument("Username")), , Command.Username)
                Command.Respond StringFormatA("User {0}{1}{0} IPBanned.", Chr$(34), Command.Argument("Username"))
            End If
        End If
    End If
End Sub

Public Sub OnIPBans(Command As clsCommandObj)
    Select Case LCase$(Command.Argument("SubCommand"))
        Case "on":
            BotVars.IPBans = True
            Call WriteINI("Other", "IPBans", "Y")
            Command.Respond "IP banning activated."
            g_Channel.CheckUsers
            
        Case "off":
            BotVars.IPBans = False
            Call WriteINI("Other", "IPBans", "N")
            Command.Respond "IP banning deactivated."
        
        Case Else:
            Command.Respond StringFormatA("IP banning is currently {0}activated.", _
                IIf(BotVars.IPBans, vbNullString, "de"))
    End Select
End Sub

Public Sub OnKickOnYell(Command As clsCommandObj)
    Select Case LCase$(Command.Argument("SubCommand"))
        Case "on":
            BotVars.KickOnYell = 1
            Call WriteINI("Other", "KickOnYell", "Y")
            Command.Respond "Kick-on-yell enabled."
            
        Case "off":
            BotVars.KickOnYell = 0
            Call WriteINI("Other", "KickOnYell", "N")
            Command.Respond "Kick-on-yell disabled."
        
        Case Else:
            Command.Respond StringFormatA("Kick-on-yell is {0}.", _
                IIf(BotVars.KickOnYell = 1, "enabled", "disabled"))
    End Select
End Sub


Public Sub OnLevelBan(Command As clsCommandObj)
    Dim Level As Integer
    If (LenB(Command.Argument("Level")) > 0) Then
        Level = Command.Argument("Level")
        If (Level > 0) Then
            If (Level < 256) Then
                Command.Respond StringFormatA("Banning Warcraft III users under level {0}.", Level)
                BotVars.BanUnderLevel = CByte(Level)
            Else
                Command.Respond "Error: Invalid level specified."
            End If
        Else
            BotVars.BanUnderLevel = 0
            Command.Respond "Levelbans disabled."
        End If
        Call WriteINI("Other", "BanUnderLevel", BotVars.BanUnderLevel)
    Else
        If (BotVars.BanUnderLevel = 0) Then
            Command.Respond "Currently not banning Warcraft III users by level."
        Else
            Command.Respond StringFormatA("Currently banning Warcraft III users under level {0}.", BotVars.BanUnderLevel)
        End If
    End If
End Sub

Public Sub OnPeonBan(Command As clsCommandObj)
    ' This command will enable, disable, or check the status of, WarCraft III peon
    ' banning.  The "Peon" class is defined by Battle.net, and is currently the lowest
    ' ranking WarCraft III user classification, in which, users have less than twenty-five
    ' wins on record for any given race.
    Select Case LCase$(Command.Argument("SubCommand"))
        Case "on":
            BotVars.BanPeons = 1
            Call WriteINI("Other", "PeonBans", "Y")
            Command.Respond "Peon banning activated."
            
        Case "off":
            BotVars.BanPeons = 0
            Call WriteINI("Other", "PeonBans", "N")
            Command.Respond "Peon banning deactivated."
        
        Case Else:
            Command.Respond StringFormatA("The bot is currently {0}banning peons.", _
                IIf(BotVars.KickOnYell = 1, vbNullString, "not "))
    End Select
End Sub

Public Sub OnPhraseBans(Command As clsCommandObj)
    Select Case LCase$(Command.Argument("SubCommand"))
        Case "on":
            PhraseBans = True
            Call WriteINI("Other", "PhraseBans", "Y")
            Command.Respond "Phrasebans activated."
            
        Case "off":
            PhraseBans = False
            Call WriteINI("Other", "PhraseBans", "N")
            Command.Respond "Phrasebans deactivated."
        
        Case Else:
            Command.Respond StringFormatA("Phrasebans are currently {0}.", _
                IIf(PhraseBans, "enabled", "disabled"))
    End Select
End Sub

Public Sub OnPlugBan(Command As clsCommandObj)
    ' This command will enable, disable, or check the status of, UDP plug bans.
    ' UDP plugs were traditionally used, in place of lag bars, to signifiy
    ' that a user was incapable of hosting (or possibly even joining) a game.
    ' However, as bot development became more popular, the emulation of such
    ' a connectivity issue became fairly common, and the UDP plug began to
    ' represent that a user was using a bot.  This feature allows for the
    ' banning of both, potential bots, and users unlikely to be capable of
    ' creating and/or joining games based on the UDP protocl.
        Select Case LCase$(Command.Argument("SubCommand"))
        Case "on":
            If (BotVars.PlugBan) Then
                Command.Respond "PlugBan is already activated."
            Else
                BotVars.PlugBan = True
                Command.Respond "PlugBan activated."
                Call WriteINI("Other", "PlugBans", "Y")
                Call g_Channel.CheckUsers
            End If
            
        Case "off":
            If (Not BotVars.PlugBan) Then
                Command.Respond "PlugBan is already deactivated."
            Else
                BotVars.PlugBan = False
                Command.Respond "PlugBan deactivated."
                Call WriteINI("Other", "PlugBans", "N")
            End If
        
        Case Else:
            Command.Respond StringFormatA("The bot is currently {0}banning people with the UDP plug.", _
                IIf(BotVars.PlugBan, vbNullString, "not "))
    End Select
End Sub

Public Sub OnPOff(Command As clsCommandObj)
    PhraseBans = False
    Call WriteINI("Other", "PhraseBans", "N")
    Command.Respond "Phrasebans deactivated."
End Sub

Public Sub OnPOn(Command As clsCommandObj)
    PhraseBans = True
    Call WriteINI("Other", "PhraseBans", "Y")
    Command.Respond "Phrasebans activated."
End Sub

Public Sub OnPStatus(Command As clsCommandObj)
    Command.Respond StringFormatA("Phrasebans are currently {0}.", _
        IIf(PhraseBans, "enabled", "disabled"))
End Sub

Public Sub OnQuietTime(Command As clsCommandObj)
    ' This command will enable, disable or check the status of, quiet time.
    ' Quiet time is a feature that will ban non-safelisted users from the
    ' channel when they speak publicly within the channel.  This is useful
    ' when a channel wishes to have a discussion while allowing public
    ' attendance, but disallowing public participation.
    Select Case LCase$(Command.Argument("SubCommand"))
        Case "on":
            BotVars.QuietTime = True
            Call WriteINI("Main", "QuietTime", "Y")
            Command.Respond "Quiet-time enabled."
            
        Case "off":
            BotVars.QuietTime = False
            Call WriteINI("Other", "QuietTime", "N")
            Command.Respond "Quiet-time disabled."
        
        Case Else:
            Command.Respond StringFormatA("The bot is currently {0}.", _
                IIf(BotVars.QuietTime, "enabled", "disabled"))
    End Select
End Sub

Public Sub OnResign(Command As clsCommandObj)
    If (Not g_Channel.Self.IsOperator) Then Exit Sub
    Call frmChat.AddQ("/resign", PRIORITY.SPECIAL_MESSAGE, Command.Username)
End Sub

Public Function OnShitAdd(Command As clsCommandObj)
    Dim sArgs    As String
    
    If (Command.IsValid) Then
        If (LenB(BotVars.DefaultShitlistGroup) > 0) Then
            Dim dbAccess As udtGetAccessResponse
            dbAccess = GetAccess(BotVars.DefaultShitlistGroup, "GROUP")
            If (LenB(dbAccess.Username) > 0) Then
                sArgs = "--group " & BotVars.DefaultShitlistGroup
            End If
        End If
        
        If (LenB(sArgs) = 0) Then sArgs = "+B"
        sArgs = StringFormatA("{0} {1} --type USER", Command.Argument("Username"), sArgs)
        
        If (LenB(Command.Argument("Message")) > 0) Then
            sArgs = StringFormatA("{0} --banmsg {1}", sArgs, Command.Argument("Message"))
        End If
        
        Command.Args = sArgs
        Call OnAdd(Command)
    End If
End Function

Public Sub OnShitDel(Command As clsCommandObj)
    If (Command.IsValid) Then
        Command.Args = Command.Argument("Username") & " -B --type USER"
        Call OnAdd(Command)
    Else
        Command.Respond "Error: You must specify a username."
    End If
End Sub

Public Sub OnSweepBan(Command As clsCommandObj)
    ' This command will grab the listing of users in the specified channel
    ' using Battle.net's "who" command, and will then begin banning each
    ' user from the current channel using Battle.net's "ban" command.
    If (Command.IsValid) Then
        If (g_Channel.Self.IsOperator) Then
            ' Changed 08-18-09 - Hdx - Uses the new Channel cache function, Eventually to beremoved to script
            'Call CacheChannelList(vbNullString, 255, "ban ")
            Call CacheChannelList(enReset, "ban ")
            Call frmChat.AddQ("/who " & Command.Argument("Channel"), PRIORITY.CHANNEL_MODERATION_MESSAGE, Command.Username, "request_receipt")
        Else
            Command.Respond "Error: The bot is not currently a channel operator."
        End If
    End If
End Sub

Public Sub OnSweepIgnore(Command As clsCommandObj)
    ' This command will grab the listing of users in the specified channel
    ' using Battle.net's "who" command, and will then begin ignoring each
    ' user using Battle.net's "squelch" command.  This command is often used
    ' instead of "sweepban" to temporarily ban all users on a given ip address
    ' without actually immediately banning them from the channel.  This is
    ' useful if a user wishes to stay below Battle.net's limitations on bans,
    ' and still prevent a number of users from joining the channel for a
    ' temporary amount of time.
    If (Command.IsValid) Then
        ' Changed 08-18-09 - Hdx - Uses the new Channel cache function, Eventually to be removed to script
        'Call CacheChannelList(vbNullString, 255, "squelch ")
        Call CacheChannelList(enReset, "squelch ")
        Call frmChat.AddQ("/who " & Command.Argument("Channel"), PRIORITY.CHANNEL_MODERATION_MESSAGE, Command.Username, "request_receipt")
    End If
End Sub

Public Sub OnTally(Command As clsCommandObj)
    If (VoteDuration > 0) Then
        Command.Respond Voting(BVT_VOTE_TALLY)
    Else
        Command.Respond "No vote is currently in progress."
    End If
End Sub

Public Sub OnUnExile(Command As clsCommandObj)
    If (Command.IsValid) Then
        Call OnShitDel(Command)
        Call OnUnIPBan(Command)
    Else
        Command.Respond "Error: You must specify a user to unban."
    End If
End Sub

Public Sub OnUnIPBan(Command As clsCommandObj)
    If (Command.IsValid) Then
        If (g_Channel.Self.IsOperator) Then
            Call frmChat.AddQ("/unsquelch " & Command.Argument("Username"), , Command.Username)
            Call frmChat.AddQ("/unban " & Command.Argument("Username"), , Command.Username)
            Command.Respond StringFormatA("User {0}{1}{0} has been Un-IPBanned.", Chr$(34), Command.Argument("Username"))
        Else
            Command.Respond "Error: The bot is not currently a channel operator."
        End If
    End If
End Sub

Private Function GetOpsCount(Optional strIgnore As String = vbNullString) As Integer
    Dim I As Integer
    For I = 1 To g_Channel.Users.Count
        If (Not StrComp(g_Channel.Users(I).DisplayName, strIgnore, vbBinaryCompare) = 0) Then
            If (g_Channel.Users(I).IsOperator) Then GetOpsCount = GetOpsCount + 1
        End If
    Next I
End Function


'This is called in ChannelBan/Squelch to reset, and cache the users in a channel
'Then again in frmChat.tmrCache, to get a list of users and ban/squelch them
'The CS/CB commands AddQ "/Who Channel" and have a "request_receipt" message
'Then in Event_ServerInfo() it checks if it has a "request_receipt" if it does it call this function
'This is vary fucking ugly, we NEED to move this to a script whenever possible.
'Public Function CacheChannelList(ByVal Inpt As String, ByVal Mode As Byte, Optional ByRef Typ As String) As String
Public Function CacheChannelList(ByVal eMode As CacheChanneListEnum, ByRef Data As String) As String
    Static colData             As New Collection
    Static sToken              As String
    Static bChannelListFollows As Boolean 'renamed this variable for clarity
    
    Dim sTemp As String
    
    'If(Mode = 255) Then
    If (eMode = enReset) Then
        Do While colData.Count > 0
            colData.Remove 1
        Loop
        sToken = Data
        bChannelListFollows = False
    End If
    
    If (Not InStr(1, LCase$(Data), "users in channel ", vbTextCompare) = 0) Then
        ' we weren't expecting a channel list, but we are now
        bChannelListFollows = True
    Else
        If (bChannelListFollows = True) Then ' if we're expecting a channel list, process it
            Select Case (eMode)
                Case enRetrieve ' RETRIEVE
                    ' Merge all the cache array items into one comma-delimited string
                    Do While colData.Count > 0
                        sTemp = StringFormatA("{0}{1}, ", sTemp, colData.Item(1))
                        colData.Remove 1
                    Loop
                    Data = sToken
                    CacheChannelList = sTemp
                Case enAdd  ' ADD
                    colData.Add Data
            End Select
        End If
    End If
End Function

