Option Strict Off
Option Explicit On
Friend Class clsMCPHandler
	'---------------------------------------------------------------------------------------
	' Module    : clsMCPHandler
	' DateTime  : 3/16/2004 23:00
	' Author    : Stealth
	' Purpose   : Handle and dispatch MCP packets
	' Thanks    : Arta[vL]'s BNetDocs, DarkRaven
	' Notes     : Goes with Stealth's modified DM DataBuffer class.
	' Update    : 4/11/06 to fix realm connection issues; added CharListResponse event
	'---------------------------------------------------------------------------------------
	
	' Requires this function placed in a module:
	'Public Function A2Hash(ByVal Password As String, ByVal ServerToken As Long) As String
	'    Dim Hash As String
	'
	'    Hash = String(5 * 4, vbNullChar)
	'    Call A2(Hash, ServerToken)
	'
	'    A2Hash = Hash
	'End Function
	
	Private Const OBJECT_NAME As String = "clsMCPHandler"
	
	Private Const MCP_STARTUP As Byte = &H1
	Private Const MCP_CHARCREATE As Byte = &H2
	Private Const MCP_CHARLOGON As Byte = &H7
	Private Const MCP_CHARDELETE As Byte = &HA
	Private Const MCP_MOTD As Byte = &H12
	Private Const MCP_CHARUPGRADE As Byte = &H18
	Private Const MCP_CHARLIST2 As Byte = &H19
	
	Private Const REALM_PASSWORD As String = "password"
	
	Private m_RealmError As Boolean
	Private m_RealmServerList As Collection
	Private m_RealmServerIndex As Short
	Private m_MCPData As String
	Private m_UniqueUsername As String
	Private m_IP As String
	Private m_Port As Integer
	Private m_CharacterList As Collection
	Private m_RealmCharacterPromptActive As Boolean
	Private m_AutoChooseWait As Integer
	Private m_AutoChooseTarget As Short
	Private m_RetrievingCharacterList As Boolean
	Private m_RealmServerConnected As Boolean
	
	'UPGRADE_NOTE: Class_Terminate was upgraded to Class_Terminate_Renamed. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="A9E4979A-37FA-4718-9994-97DD76ED70A7"'
	Private Sub Class_Terminate_Renamed()
		'UPGRADE_NOTE: Object m_RealmServerList may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		m_RealmServerList = Nothing
		'UPGRADE_NOTE: Object m_CharacterList may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		m_CharacterList = Nothing
	End Sub
	Protected Overrides Sub Finalize()
		Class_Terminate_Renamed()
		MyBase.Finalize()
	End Sub
	
	' use this to clear the list of characters (before sending MCP_CHARLIST2)
	Public Sub ClearInternalCharacters()
		m_CharacterList = New Collection
	End Sub
	
	' adds a realm server structure to the RealmList
	Public Sub SetRealmServerInfo(ByRef List() As Object)
		Dim i As Short
		
		m_RealmServerList = New Collection
		
		For i = LBound(List) To UBound(List)
			m_RealmServerList.Add(List(i))
		Next i
	End Sub
	
	' asks for the relam to join
	' saves the information of the chosen realm as the one "chosen" or if index is 0 there were no realms
	' uses [Override] RealmServer= value if present
	Public Function ChooseRealm(ByRef sTitle As String, ByRef ChoiceValue As String) As Boolean
		On Error GoTo ERROR_HANDLER
		
		Dim Realm() As String
		Dim i As Short
		Dim Title As String
		Dim Descr As String
		
		ChoiceValue = Config.RealmAutoChooseServer
		
		' loop through realms
		For i = 1 To m_RealmServerList.Count()
			'UPGRADE_WARNING: Couldn't resolve default property of object m_RealmServerList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
			Realm = m_RealmServerList.Item(i)
			
			If (StrComp(Realm(0), ChoiceValue, CompareMethod.Text) = 0) Then
				' choose this, preserve i
				Exit For
			ElseIf StrictIsNumeric(ChoiceValue) Then 
				If i = CShort(ChoiceValue) Then
					' choose this, preserve i
					Exit For
				End If
			End If
		Next i
		
		If i > 0 And i <= m_RealmServerList.Count() Then
			' something was set, use that
			'UPGRADE_WARNING: Couldn't resolve default property of object m_RealmServerList.Item()(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
			sTitle = m_RealmServerList.Item(i)(0)
			ChooseRealm = True
		Else
			' nothing was found, return fail
			sTitle = vbNullString
			' the return is true if there are realms but we didn't select an existing one!
			ChooseRealm = False
			
			' unless...
            If Len(ChoiceValue) = 0 And m_RealmServerList.Count() > 0 Then
                ' if nothing was set, use first realm they list
                'UPGRADE_WARNING: Couldn't resolve default property of object m_RealmServerList.Item()(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
                sTitle = m_RealmServerList.Item(1)(0)
                ChooseRealm = True
            End If
		End If
		
		Exit Function
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.ChooseRealm()", Err.Number, Err.Description, OBJECT_NAME))
	End Function
	
	' call this to handle the completed querying of realm servers
	Public Sub HandleQueryRealmServersResponse()
		On Error GoTo ERROR_HANDLER
		Dim sTitle As String
		Dim sUserChoice As String
		
		If ds.EnteredChatFirstTime Then
			SEND_SID_LEAVECHAT()
		End If
		
		frmChat.mnuRealmSwitch.Enabled = False
		
		If Not ds.MCPHandler Is Nothing Then
			If ds.MCPHandler.ChooseRealm(sTitle, sUserChoice) Then
                If Len(sTitle) > 0 Then
                    Call ds.MCPHandler.RealmServerLogon(sTitle)
                Else
                    ' shouldn't happen
                    Call frmChat.AddChat(RTBColors.ErrorMessageText, "[BNCS] Realm logon error: That character was not found.")
                    Call SendEnterChatSequence()
                    frmChat.mnuRealmSwitch.Enabled = True
                End If
            ElseIf Len(sUserChoice) > 0 Then
                Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("[BNCS] The realm {0}{1}{0} is offline or doesn't exist.", Chr(34), sUserChoice))
                Call SendEnterChatSequence()
                frmChat.mnuRealmSwitch.Enabled = True
			Else
				Call frmChat.AddChat(RTBColors.ErrorMessageText, "[BNCS] All Diablo II realms are currently offline.")
				Call SendEnterChatSequence()
				frmChat.mnuRealmSwitch.Enabled = True
			End If
		End If
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.HandleQueryRealmServersResponse()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	' call this to begin realm connection (logon via BNCS SID_LOGONREALMEX)
	' the given realm must be known by name (stored with SetRealmServerInfo)
	Public Sub RealmServerLogon(ByVal sTitle As String)
		On Error GoTo ERROR_HANDLER
		
		Dim i As Short
		Dim Realm() As String
		Dim sPass As String
		
		m_RealmServerConnected = False
		
		For i = 1 To m_RealmServerList.Count()
			'UPGRADE_WARNING: Couldn't resolve default property of object m_RealmServerList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
			Realm = m_RealmServerList.Item(i)
			If (StrComp(Realm(0), sTitle, CompareMethod.Text) = 0) Then
				m_RealmServerIndex = i
				
				sPass = GetRealmPassword()
				
				SEND_SID_LOGONREALMEX(sTitle, sPass)
				
				Exit Sub
			End If
		Next i
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.RealmServerLogon()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	' sets the data to store for the realm server connection
	' call this after receiving SID_LOGONREALMEX
	Public Sub SetStartupData(ByRef MCPData As String, ByRef UniqueUsername As String, ByRef IP As String, ByRef Port As Integer)
		m_MCPData = MCPData
		m_UniqueUsername = UniqueUsername
		m_IP = IP
		m_Port = Port
	End Sub
	
	' gets the realm password (overridable)
	Private Function GetRealmPassword() As String
		Dim RealmPassword As String
		
		RealmPassword = Config.RealmServerPassword
		
        If (Len(RealmPassword) = 0) Then RealmPassword = REALM_PASSWORD
		
		GetRealmPassword = RealmPassword
	End Function
	
	' sends a request for the character list
	Public Sub DoRequestCharacters()
		m_RetrievingCharacterList = True
		
		SEND_MCP_CHARLIST2()
	End Sub
	
	' parses packets as received from sckMCP
    Public Sub ParsePacket(ByVal Data() As Byte)
        On Error GoTo ERROR_HANDLER
        Static pBuff As New clsDataBuffer

        Dim PacketID As Byte

        'MCPRecvPacket = True
        With pBuff
            .Clear()
            .Data = Data
            .GetWord()
            PacketID = .GetByte
        End With

        If (MDebug("all")) Then
            frmChat.AddChat(COLOR_BLUE, StringFormat("MCP RECV 0x{0}", ZeroOffset(PacketID, 2)))
        End If

        CachePacket(modEnum.enuPL_DirectionTypes.StoC, modEnum.enuPL_ServerTypes.stMCP, PacketID, Len(Data), Data)

        ' Added 2007-06-08 for a packet logging menu feature to aid tech support
        WritePacketData(modEnum.enuErrorSources.MCP, modEnum.enuPL_DirectionTypes.StoC, PacketID, Len(Data), Data)

        If (RunInAll("Event_PacketReceived", "MCP", PacketID, Len(Data), Data)) Then
            Exit Sub
        End If

        Select Case PacketID

            Case MCP_STARTUP : Call RECV_MCP_STARTUP(pBuff)
            Case MCP_CHARCREATE : Call RECV_MCP_CHARCREATE(pBuff)
            Case MCP_CHARLOGON : Call RECV_MCP_CHARLOGON(pBuff)
            Case MCP_CHARDELETE : Call RECV_MCP_CHARDELETE(pBuff)
            Case MCP_MOTD : Call RECV_MCP_MOTD(pBuff)
            Case MCP_CHARLIST2 : Call RECV_MCP_CHARLIST2(pBuff)
            Case MCP_CHARUPGRADE : Call RECV_MCP_CHARUPDGRADE(pBuff)

            Case Else
                If (MDebug("debug") And (MDebug("all") Or MDebug("unknown"))) Then
                    Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("[REALM] Unhandled packet 0x{0}", ZeroOffset(CInt(PacketID), 2)))
                    Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("[REALM] Packet data: {0}{1}", vbNewLine, DebugOutput(Data)))
                End If

        End Select

        Exit Sub
ERROR_HANDLER:
        Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.ParsePacket()", Err.Number, Err.Description, OBJECT_NAME))
    End Sub
	
	'*******************************
	'MCP_STARTUP (0x01) S->C
	'*******************************
	' (DWORD) Result
	'*******************************
	Private Sub RECV_MCP_STARTUP(ByRef pBuff As clsDataBuffer)
		Dim Result As Integer
		
		'(DWORD)      Result
		Result = pBuff.GetDWORD
		'0x00: Success
		'0x0C: No Battle.net connection detected
		'Debug.Print n
		
		Select Case Result
			Case &H0 : Call Event_RealmStartup(True, "Connected to Diablo II realm.")
			Case &H2, &HA, &HB, &HC, &HD : Call Event_RealmStartup(False, "The server thinks you're not connected to Battle.net! Please try your connection again later.")
			Case &H7E : Call Event_RealmStartup(False, "The Diablo II realm rejected your connection. Your CD-Key may have been banned from realm play. Please try your connection again later.")
			Case &H7F : Call Event_RealmStartup(False, "You have been temporarily IP-banned from the Realm server. Please try connecting again later.")
			Case Else : Call Event_RealmStartup(False, "Unknown response to MCP_STARTUP: 0x" & Right("00000000" & Hex(Result), 4))
		End Select
	End Sub
	
	'*******************************
	'MCP_CHARCREATE (0x02) S->C
	'*******************************
	' (DWORD) Result
	'*******************************
	Private Sub RECV_MCP_CHARCREATE(ByRef pBuff As clsDataBuffer)
		Dim Result As Integer
		
		'(DWORD)      Result
		Result = pBuff.GetDWORD
		'0x00: Success
		'0x14: Character already exists, or maximum number of characters (currently 8) reached.
		'0x15: Invalid Name
		
		Select Case Result
			Case &H0 : Call Event_CharCreateResponse(True, "Character created.")
			Case &H14 : Call Event_CharCreateResponse(False, "That character name already exists.")
			Case &H15 : Call Event_CharCreateResponse(False, "That character name is invalid.")
			Case Else : Call Event_CharCreateResponse(False, "Unknown response to MCP_CHARCREATE: 0x" & Right("00000000" & Hex(Result), 4))
		End Select
	End Sub
	
	'*******************************
	'MCP_CHARLOGON (0x07) S->C
	'*******************************
	' (DWORD) Result
	'*******************************
	Private Sub RECV_MCP_CHARLOGON(ByRef pBuff As clsDataBuffer)
		Dim Result As Integer
		
		'(DWORD)      Result
		Result = pBuff.GetDWORD
		'0 x00: Success
		'0 x46: Player Not Found
		
		Select Case Result
			Case &H0 : Call Event_CharLogonResponse(True, "Realm logon successful.")
			Case &H46 : Call Event_CharLogonResponse(False, "That character was not found.")
			Case &H7A : Call Event_CharLogonResponse(False, "Unable to log on to realm character!")
			Case &H7B : Call Event_CharLogonResponse(False, "That character has expired.")
			Case Else : Call Event_CharLogonResponse(False, "Unknown response to MCP_CHARLOGON: 0x" & Right("00000000" & Hex(Result), 4))
		End Select
	End Sub
	
	'*******************************
	'MCP_CHARDELETE (0x0A) S->C
	'*******************************
	' (DWORD) Result
	'*******************************
	Private Sub RECV_MCP_CHARDELETE(ByRef pBuff As clsDataBuffer)
		Dim Result As Integer
		
		'(DWORD)      Result
		Result = pBuff.GetDWORD
		
		Select Case Result
			Case &H0 : Call Event_CharDeleteResponse(True, "Character deleted.")
			Case &H49 : Call Event_CharDeleteResponse(False, "That character was not found.")
			Case Else : Call Event_CharDeleteResponse(False, "Unknown response to RECV_MCP_CHARDELETE: 0x" & Right("00000000" & Hex(Result), 4))
		End Select
	End Sub
	
	'*******************************
	'MCP_MOTD (0x12) S->C
	'*******************************
	' (BYTE) Unknown
	' (STRING) Message of the day
	'*******************************
	Private Sub RECV_MCP_MOTD(ByRef pBuff As clsDataBuffer)
		Dim Message As String
		
		'(STRING)         Unknown - perhaps a headline?
		'ignored
		pBuff.GetByte()
		Message = pBuff.GetString
		'(STRING)         MOTD
		Call Event_RealmMOTD(Message)
	End Sub
	
	'*******************************
	'MCP_CHARUPDGRADE (0x18) S->C
	'*******************************
	' (DWORD) Result
	'*******************************
	Private Sub RECV_MCP_CHARUPDGRADE(ByRef pBuff As clsDataBuffer)
		Dim Result As Integer
		
		'(DWORD)      Result
		Result = pBuff.GetDWORD
		
		Select Case Result
			Case &H0 : Call Event_CharUpgradeResponse(True, "Character upgrade successful.")
			Case &H46 : Call Event_CharUpgradeResponse(False, "That character was not found.")
			Case &H7A : Call Event_CharUpgradeResponse(False, "Upgrade failed.")
			Case &H7B : Call Event_CharUpgradeResponse(False, "That character has expired.")
			Case &H7C : Call Event_CharUpgradeResponse(False, "That is already an expansion character.")
			Case Else : Call Event_CharUpgradeResponse(False, "Unknown response to RECV_MCP_CHARDELETE: 0x" & Right("00000000" & Hex(Result), 4))
		End Select
	End Sub
	
	'*******************************
	'MCP_STARTUP (0x01) C->S
	'*******************************
	' (DWORD)[16] MCP startup data
	' (STRING) BNCS Unique username
	'*******************************
	Public Sub SEND_MCP_STARTUP()
		On Error GoTo ERROR_HANDLER
		
		Dim pBuff As New clsDataBuffer
		pBuff.InsertNonNTString(m_MCPData)
		pBuff.InsertNTString(m_UniqueUsername)
		pBuff.SendPacketMCP(MCP_STARTUP)
		'UPGRADE_NOTE: Object pBuff may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		pBuff = Nothing
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.SEND_MCP_STARTUP()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	'*******************************
	'MCP_CHARLIST (0x19) S->C
	'*******************************
	' (WORD) Number of characters requested
	' (DWORD) Number of characters that exist on this account
	' (WORD) Number of characters returned
	' For each character:
	'     (DWORD) Expiration Date
	'     (STRING) Character name
	'     (STRING) Character statstring
	'*******************************
	Private Sub RECV_MCP_CHARLIST2(ByRef pBuff As clsDataBuffer)
		Dim CharCount As Short
		Dim i As Short
		Dim seconds As Integer
		Dim Expiry As Date
		Dim CharName As String
		Dim CharStats As String
		Dim CharStatsObj As clsUserStats
		
		pBuff.GetWord()
		pBuff.GetWord()
		pBuff.GetWord()
		CharCount = pBuff.GetWord
		
		m_CharacterList = New Collection
		
		For i = 1 To CharCount
			seconds = pBuff.GetDWORD
			CharName = pBuff.GetString
			CharStats = pBuff.GetString
			
			CharStatsObj = New clsUserStats
			CharStatsObj.Statstring = BotVars.Product & RealmServerTitle(RealmServerSelectedIndex) & "," & CharName & "," & CharStats
			Expiry = DateAdd(Microsoft.VisualBasic.DateInterval.Second, seconds, CDate("1/1/1970"))
			
			'UPGRADE_WARNING: Array has a new behavior. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="9B7D5ADD-D8FE-4819-A36C-6DEDAF088CC7"'
			m_CharacterList.Add(New Object(){CharName, CharStatsObj, Expiry})
		Next i
		
		Call Event_CharListResponse()
	End Sub
	
	
	'*******************************
	'MCP_CHARCREATE (0x02) C->S
	'*******************************
	' (DWORD) Character class ID
	' (WORD) Character flags
	' (STRING) Character name
	'*******************************
	Public Sub SEND_MCP_CHARCREATE(ByVal CharType As Integer, ByVal CharFlags As Integer, ByVal CharName As String)
		On Error GoTo ERROR_HANDLER
		
		Dim pBuff As New clsDataBuffer
		pBuff.InsertDWord(CharType)
		pBuff.InsertWord(CharFlags)
		pBuff.InsertNTString(CharName)
		pBuff.SendPacketMCP(MCP_CHARCREATE)
		'UPGRADE_NOTE: Object pBuff may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		pBuff = Nothing
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.SEND_MCP_CHARCREATE()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	'*******************************
	'MCP_CHARLOGON (0x07) C->S
	'*******************************
	' (STRING) Character name
	'*******************************
	Public Sub SEND_MCP_CHARLOGON(ByVal CharName As String)
		On Error GoTo ERROR_HANDLER
		
		Dim pBuff As New clsDataBuffer
		pBuff.InsertNTString(CharName)
		pBuff.SendPacketMCP(MCP_CHARLOGON)
		'UPGRADE_NOTE: Object pBuff may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		pBuff = Nothing
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.SEND_MCP_CHARLOGON()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	'*******************************
	'MCP_CHARDELETE (0x0A) C->S
	'*******************************
	' (WORD) Unknown
	' (STRING) Character name
	'*******************************
	Public Sub SEND_MCP_CHARDELETE(ByVal CharName As String, Optional ByVal Flags As Short = &H0)
		On Error GoTo ERROR_HANDLER
		
		Dim pBuff As New clsDataBuffer
		pBuff.InsertWord(Flags)
		pBuff.InsertNTString(CharName)
		pBuff.SendPacketMCP(MCP_CHARDELETE)
		'UPGRADE_NOTE: Object pBuff may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		pBuff = Nothing
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.SEND_MCP_CHARDELETE()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	'*******************************
	'MCP_MOTD (0x12) C->S
	'*******************************
	' [Blank]
	'*******************************
	Public Sub SEND_MCP_MOTD()
		On Error GoTo ERROR_HANDLER
		
		Dim pBuff As New clsDataBuffer
		pBuff.SendPacketMCP(MCP_MOTD)
		'UPGRADE_NOTE: Object pBuff may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		pBuff = Nothing
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.SEND_MCP_MOTD()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	'*******************************
	'MCP_CHARUPGRADE (0x18) C->S
	'*******************************
	' (STRING) Character name
	'*******************************
	Public Sub SEND_MCP_CHARUPGRADE(ByVal CharName As String)
		On Error GoTo ERROR_HANDLER
		
		Dim pBuff As New clsDataBuffer
		pBuff.InsertNTString(CharName)
		pBuff.SendPacketMCP(MCP_CHARUPGRADE)
		'UPGRADE_NOTE: Object pBuff may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		pBuff = Nothing
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.SEND_MCP_CHARUPGRADE()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	'*******************************
	'MCP_CHARLIST2 (0x19) C->S
	'*******************************
	' (DWORD) Number of characters
	'*******************************
	Public Sub SEND_MCP_CHARLIST2(Optional ByVal NumCharacters As Object = 8)
		On Error GoTo ERROR_HANDLER
		
		Dim pBuff As New clsDataBuffer
		'UPGRADE_WARNING: Couldn't resolve default property of object NumCharacters. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
		pBuff.InsertDWord(NumCharacters)
		pBuff.SendPacketMCP(MCP_CHARLIST2)
		'UPGRADE_NOTE: Object pBuff may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
		pBuff = Nothing
		
		Exit Sub
ERROR_HANDLER: 
		Call frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("Error: #{0}: {1} in {2}.SEND_MCP_CHARLIST2()", Err.Number, Err.Description, OBJECT_NAME))
	End Sub
	
	Public ReadOnly Property UniqueUsername() As String
		Get
			UniqueUsername = m_UniqueUsername
		End Get
	End Property
	
	Public ReadOnly Property RealmServerCount() As Short
		Get
			RealmServerCount = m_RealmServerList.Count()
		End Get
	End Property
	
	Public ReadOnly Property RealmServerSelectedIndex() As Short
		Get
			RealmServerSelectedIndex = m_RealmServerIndex - 1
		End Get
	End Property
	
	Public ReadOnly Property RealmServerTitle(ByVal RealmIndex As Short) As String
		Get
			Dim Server() As String
			
			If RealmIndex >= 0 And RealmIndex < m_RealmServerList.Count() Then
				'UPGRADE_WARNING: Couldn't resolve default property of object m_RealmServerList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				Server = m_RealmServerList.Item(RealmIndex + 1)
				RealmServerTitle = Server(0)
			Else
				RealmServerTitle = vbNullString
			End If
		End Get
	End Property
	
	Public ReadOnly Property RealmServerDescription(ByVal RealmIndex As Short) As String
		Get
			Dim Server() As String
			
			If RealmIndex >= 0 And RealmIndex < m_RealmServerList.Count() Then
				'UPGRADE_WARNING: Couldn't resolve default property of object m_RealmServerList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				Server = m_RealmServerList.Item(RealmIndex + 1)
				RealmServerDescription = Server(1)
			Else
				RealmServerDescription = vbNullString
			End If
		End Get
	End Property
	
	Public ReadOnly Property RealmSelectedServerIP() As String
		Get
			RealmSelectedServerIP = m_IP
		End Get
	End Property
	
	Public ReadOnly Property RealmSelectedServerPort() As Integer
		Get
			RealmSelectedServerPort = m_Port
		End Get
	End Property
	
	Public ReadOnly Property CharacterCount() As Short
		Get
			CharacterCount = m_CharacterList.Count()
		End Get
	End Property
	
	Public ReadOnly Property CharacterName(ByVal Index As Short) As String
		Get
			Dim Character() As Object
			
			If Index >= 0 And Index < m_CharacterList.Count() Then
				'UPGRADE_WARNING: Couldn't resolve default property of object m_CharacterList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				Character = m_CharacterList.Item(Index + 1)
				'UPGRADE_WARNING: Couldn't resolve default property of object Character(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				CharacterName = CStr(Character(0))
			Else
				CharacterName = vbNullString
			End If
		End Get
	End Property
	
	Public ReadOnly Property CharacterStats(ByVal Index As Short) As clsUserStats
		Get
			Dim Character() As Object
			
			If Index >= 0 And Index < m_CharacterList.Count() Then
				'UPGRADE_WARNING: Couldn't resolve default property of object m_CharacterList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				Character = m_CharacterList.Item(Index + 1)
				CharacterStats = Character(1)
			Else
				'UPGRADE_NOTE: Object CharacterStats may not be destroyed until it is garbage collected. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6E35BFF6-CD74-4B09-9689-3E1A43DF8969"'
				CharacterStats = Nothing
			End If
		End Get
	End Property
	
	Public ReadOnly Property CharacterExpires(ByVal Index As Short) As Date
		Get
			Dim Character() As Object
			
			If Index >= 0 And Index < m_CharacterList.Count() Then
				'UPGRADE_WARNING: Couldn't resolve default property of object m_CharacterList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				Character = m_CharacterList.Item(Index + 1)
				'UPGRADE_WARNING: Couldn't resolve default property of object Character(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				CharacterExpires = CDate(Character(2))
			Else
				CharacterExpires = System.Date.FromOADate(0)
			End If
		End Get
	End Property
	
	Public ReadOnly Property AutoChooseWait() As Integer
		Get
			AutoChooseWait = m_AutoChooseWait
		End Get
	End Property
	
	Public ReadOnly Property AutoChooseTarget() As Short
		Get
			AutoChooseTarget = m_AutoChooseTarget
		End Get
	End Property
	
	Public ReadOnly Property RetrievingCharacterList() As Boolean
		Get
			RetrievingCharacterList = m_RetrievingCharacterList
		End Get
	End Property
	
	Public ReadOnly Property RealmServerConnected() As Boolean
		Get
			RealmServerConnected = m_RealmServerConnected
		End Get
	End Property
	
	Public Property FormActive() As Boolean
		Get
			FormActive = m_RealmCharacterPromptActive
		End Get
		Set(ByVal Value As Boolean)
			m_RealmCharacterPromptActive = Value
		End Set
	End Property
	
	Public Property IsRealmError() As Boolean
		Get
			IsRealmError = m_RealmError
		End Get
		Set(ByVal Value As Boolean)
			m_RealmError = Value
		End Set
	End Property
	
	Private Sub Event_RealmStartup(ByVal Success As Boolean, ByVal Message As String)
		If Success Then
			'frmChat.AddChat RTBColors.SuccessText, "[REALM] " & Message
			
			If m_RealmCharacterPromptActive Then
				frmRealm.RealmStartupResponse()
			End If
			
			DoRequestCharacters()
		Else
			frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] " & Message)
			
			If ds.MCPHandler.FormActive Then
				frmRealm.UnloadRealmError()
			End If
			
			SendEnterChatSequence()
			frmChat.mnuRealmSwitch.Enabled = True
		End If
	End Sub
	
	Private Sub Event_CharCreateResponse(ByVal Success As Boolean, ByVal Message As String)
		If Success Then
			frmChat.AddChat(RTBColors.SuccessText, "[REALM] " & Message)
		Else
			frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] " & Message)
		End If
		
		If m_RealmCharacterPromptActive Then
			Call frmRealm.CharCreateResponse(Success, Message)
		End If
	End Sub
	
	Private Sub Event_CharDeleteResponse(ByVal Success As Boolean, ByVal Message As String)
		If Success Then
			frmChat.AddChat(RTBColors.SuccessText, "[REALM] " & Message)
		Else
			frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] " & Message)
		End If
		
		If m_RealmCharacterPromptActive Then
			Call frmRealm.CharDeleteResponse(Success, Message)
		End If
	End Sub
	
	Private Sub Event_CharLogonResponse(ByVal Success As Boolean, ByVal Message As String)
		If Success Then
			frmChat.AddChat(RTBColors.SuccessText, "[REALM] " & Message)
			
			SEND_MCP_MOTD()
			
			SendEnterChatSequence()
			frmChat.mnuRealmSwitch.Enabled = True
		Else
			frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] " & Message)
			
			m_RealmError = True
		End If
		
		If m_RealmCharacterPromptActive Then
			Call frmRealm.CharLogonResponse(Success, Message)
		End If
	End Sub
	
	Private Sub Event_RealmMOTD(ByVal Message As String)
		Dim RealmHideMotd As Boolean
		
		RealmHideMotd = Config.RealmHideMotd
		
		If Not RealmHideMotd Then
			frmChat.AddChat(RTBColors.ServerInfoText, "[REALM] " & Message)
		End If
	End Sub
	
	Private Sub Event_CharUpgradeResponse(ByVal Success As Boolean, ByVal Message As String)
		If Success Then
			frmChat.AddChat(RTBColors.SuccessText, "[REALM] " & Message)
		Else
			frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] " & Message)
		End If
		
		If m_RealmCharacterPromptActive Then
			Call frmRealm.CharUpgradeResponse(Success, Message)
		End If
	End Sub
	
	Private Sub Event_CharListResponse()
		Dim RealmCharacter As String
		Dim RealmCharIndex As Short
		Dim RealmAutoChooseWait As Integer
		Dim i As Short
		Dim Character() As Object
		Dim CharStats As clsUserStats
		Dim CharDate As Date
		
		m_RetrievingCharacterList = False
		
		RealmCharacter = Config.RealmAutoChooseCharacter
		RealmAutoChooseWait = Config.RealmAutoChooseDelay
		
		If m_CharacterList.Count() = 0 Then
			' nothing can be chosen
			RealmCharIndex = -1
		Else
            If Len(RealmCharacter) > 0 Then
                ' invalid choice
                RealmCharIndex = -1

                For i = 1 To m_CharacterList.Count()
                    ' user entered a character name?
                    'UPGRADE_WARNING: Couldn't resolve default property of object m_CharacterList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
                    Character = m_CharacterList.Item(i)
                    'UPGRADE_WARNING: Couldn't resolve default property of object Character(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
                    If (StrComp(Character(0), RealmCharacter, CompareMethod.Text) = 0) Then
                        ' choose by name
                        RealmCharIndex = i - 1
                        Exit For
                        ' user chose a character index?
                    ElseIf StrictIsNumeric(RealmCharacter) Then
                        If i = CShort(RealmCharacter) Then
                            ' choose by index
                            RealmCharIndex = i - 1
                            Exit For
                        End If
                    End If
                Next i
            Else
                ' no entry, choose first character after X seconds
                RealmCharIndex = 0
            End If
		End If
		
		If ds.EnteredChatFirstTime Then
			' always disable timer if they chose to switch character
			RealmAutoChooseWait = -1
		End If
		
		m_RealmServerConnected = True
		
		If m_RealmCharacterPromptActive Then
			' if the form is already open, tell the form to re-read the character list
			If m_CharacterList.Count() = 0 Then
				frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] There are no characters on this account.")
			Else
				frmChat.AddChat(RTBColors.SuccessText, "[REALM] Listed " & m_CharacterList.Count() & " characters.")
			End If
			frmRealm.CharListResponse()
			' set focus
			On Error Resume Next
			frmRealm.Activate()
			
		ElseIf RealmCharIndex >= 0 Then 
			' valid entry: choose after X seconds
			m_AutoChooseWait = RealmAutoChooseWait
			m_AutoChooseTarget = RealmCharIndex
			If m_AutoChooseWait = 0 Then
				'UPGRADE_WARNING: Couldn't resolve default property of object m_CharacterList.Item(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				Character = m_CharacterList.Item(m_AutoChooseTarget + 1)
				CharStats = Character(1)
				'UPGRADE_WARNING: Couldn't resolve default property of object Character(2). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
				CharDate = Character(2)
                If Len(Character(0)) > 0 Then
                    'UPGRADE_WARNING: DateDiff behavior may be different. Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6B38EC3F-686D-4B2E-B5A5-9E8E7A762E32"'
                    If (System.Math.Sign(DateDiff(Microsoft.VisualBasic.DateInterval.Second, UtcNow, CharDate)) >= 0) Then
                        ' must be PX2D if isExpansion, otherwise doesn't matter
                        If (VB6.Imp(CharStats.IsExpansionCharacter(), (BotVars.Product = "PX2D"))) Then
                            'UPGRADE_WARNING: Couldn't resolve default property of object Character(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
                            frmChat.AddChat(RTBColors.InformationText, "[REALM] Automatically logging on as " & Character(0) & "...")
                            'UPGRADE_WARNING: Couldn't resolve default property of object Character(). Click for more: 'ms-help://MS.VSCC.v90/dv_commoner/local/redirect.htm?keyword="6A50421D-15FE-4896-8A1B-2EC21E9037B2"'
                            Call SEND_MCP_CHARLOGON(Character(0))
                            Exit Sub
                        Else
                            frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] Character auto-logon fail: You must use Diablo II: Lord of Destruction to choose that character.")
                        End If
                    Else
                        frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] Character auto-logon fail: That character has expired.")
                    End If
                Else
                    frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] Character auto-logon fail: Empty character name.")
                End If
			End If
			
			frmChat.AddChat(RTBColors.SuccessText, "[REALM] Listed " & m_CharacterList.Count() & " characters.")
			
			m_AutoChooseWait = -1
			frmRealm.Show()
			
		Else
			If m_CharacterList.Count() = 0 Then
				frmChat.AddChat(RTBColors.ErrorMessageText, "[REALM] There are no characters on this account.")
			ElseIf Not m_RealmCharacterPromptActive Then 
				frmChat.AddChat(RTBColors.ErrorMessageText, StringFormat("[REALM] There are no characters matching {0}{1}{0} to use.", Chr(34), RealmCharacter))
			End If
			' invalid entry: show realms list, choose nothing after X seconds
			m_AutoChooseWait = RealmAutoChooseWait
			m_AutoChooseTarget = -1
			frmRealm.Show()
		End If
	End Sub
	
	
	'    StatString << "PX2D";               // Product
	'    StatString << "Moo,";               // Realm
	'    StatString << "iagovL,";            // Character name, again, seems to do nothing
	'    StatString << (BYTE) 0x84; // 0x84 = nothing?
	'    StatString << (BYTE) 0x80; // 0x80 = nothing?
	'    StatString << (BYTE) 40; // 0x01 = Helmet
	'    StatString << (BYTE) 3; // Chest
	'    StatString << (BYTE) 3; // Legs
	'    StatString << (BYTE) 3; // Right Arm
	'    StatString << (BYTE) 3; // left arm
	'    StatString << (BYTE) 1; // weapon
	'    StatString << (BYTE) 1; // bow?
	'    StatString << (BYTE) 1; // shield
	'    StatString << (BYTE) 1; // right shoulder
	'    StatString << (BYTE) 1; // left shoulder
	'    StatString << (BYTE) i; // nothing
	'    StatString << (BYTE) 4; // Race - See notebook :)
	'    StatString << (BYTE) 1; // helmet color
	'    StatString << (BYTE) 1; // chest color
	'    StatString << (BYTE) 1; // leg color
	'    StatString << (BYTE) 1; // r arm color
	'    StatString << (BYTE) 1; // l arm color
	'    StatString << (BYTE) 1; // weapon color
	'    StatString << (BYTE) 1; // bow color
	'    StatString << (BYTE) 1; // shield color
	'    StatString << (BYTE) 166; // right shoulder color
	'    StatString << (BYTE) 169; // left shoulder color
	'    StatString << (BYTE) i;
	'    StatString << (BYTE) 0x63; // Level
	'    StatString << (BYTE) (0xa0); // 0xa0 = bit 0 = ?, bit 1 = ?, bit 2 = Hardcore, bit 3 = Dead
	'    StatString << (BYTE) 0x80; // 0x80 = Rank - see notebook :)
	'    StatString << (BYTE) 0xff; // ?
	'    StatString << (BYTE) 0xff; // ?
	'    StatString << (BYTE) 0xff; // ?
	'    StatString << (BYTE) 0x80; // Nothing?
	'    StatString << (BYTE) 0x80; // Nothing?
	'    StatString << (BYTE) 0x80; // Nothing?
	'    StatString << (BYTE) 0; // Null-terminator.
	'35 BYTES ABOVE
	
	'   53 68 69 67 00      Shig.   '33 BYTES BELOW
	'   [!OPEN] [] [BODY INFO      ]                                                                 [ IGNORE     ]
	'   84  80  53 03 03 03 03 12 FF  51 02 02 FF  04 ED  02 02 02 02 23 FF  04 02 02 FF  35 A8  9A  FF FF FF FF FF
	'   132 128 83 3  3  3  3  18 255 81 2  2  255 4  237 2  2  2  2  35 255  4  2  2 255 53 168 154 FF FF FF FF FF
	
	
	
	'From UserLoser:
	
	
	'0000:  50 58 32 44 55 53 57 65 73 74 2C 49 70 41 64 64   PX2DUSWest,IpAdd
	'0010:  52 65 73 53 2C '84 80 01 01 01 01 01 FF FF FF 01   ResS,��.....���.
	'0020:  01 FF 05 FF FF FF FF FF FF FF FF FF FF FF 01 A0   .�.�����������.�
	'0030:  80 FF FF FF 80 80                                 ������
	
	'0000:  50 58 32 44 55 53 45 61 73 74 2C 53 68 61 76 6F   PX2DUSEast,Shavo
	'0010:  2C 84 80 39 FF FF FF FF 18 FF 51 FF FF FF 05 4D   ,��9����.�Q���.M
	'0020:  FF FF FF FF FF FF FF FF FF FF 51 A8 9E FF FF FF   ����������Q�����
	'0030:  FF FF                                             ��
	
	'01 'Helmet -2
	'01 'Chest  -3
	'01 'Legs   -4
	'01 'Right Arm  -5
	'01 'Weapon -6
	'FF 'Bow    -7
	'FF 'Sheild -8
	'FF 'Right Shoulder     9
	'01 'Left Shoulder  10
	
	'.....���.
	
	'01 'Nothing    11
	'FF '?  12
	'05 'Charclass - 13
	'FF 'Helmet color   14
	'FF 'Chest color    15
	'FF 'Right arm color    16
	'FF 'Left arm color 17
	'FF 'Weapon color   18
	'FF 'Bow color  -19
	'FF 'Sheild color   -20
	'FF 'Right shoulder color   -21
	'FF 'Left shoulder color    -22
	'FF 'Nothing    -23
	'FF 'Nothing    -24
	'01 'Level  -25
	'A0 'bit 0 = ?, bit 1 = ?, bit 2 = Hardcore, bit 3 = Dead   26
	'.�.�����������.�
	'80 53 02 02 02 02 1C 1C FF 03 03 FF 05 ED 43 43 43 43 30 30 FF 43 43 FF 51 A4 9E FF FF FF FF FF 00
	
	'80 'Rank   27
	'FF '?  28
	'FF '?  29
	'FF '?  30
	'80 'Nothing    31
	'80 'Nothing    32
	
	
	' SHIG
	'       1   2  3 4  5  6  7  8  9  10 11 12 13 14 15 16
	'0000:  53 03 03 03 03 12 FF 51 02 02 FF 04 ED 02 02 02   S�Q��
	
	'       17 18 19 20 21 22 23 24 25 26 27 28 29 30 31
	'0010:  02 23 FF 04 02 02 FF 35 A8 9A FF FF FF FF FF      #��5�������.
	
	
	'MULEORIFFIC
	'       1   2  3 4  5  6  7  8  9  10 11 12 13 14 15 16
	'0000:  FF FF FF FF FF FF FF FF FF FF FF 02 FF FF FF FF   ���������������
	
	'       17 18 19 20 21 22 23 24 25 26 27 28 29 30 31
	'0010:  FF FF FF FF FF FF FF 01 A0 80 FF FF FF FF FF      ��������������.
	
	
	'A dumbed-down version of full d2 statstring parsing that returns Values()
	' for use in adding people to the character list listview.
	'Public Sub GetD2CharStats(ByVal sIn As String, ByRef Class As String, ByRef ClassByte As Byte, _
	''                                ByRef Level As Byte, ByRef IsHardcore As Boolean, ByRef IsDead As Boolean, _
	''                                ByRef IsLadder As Boolean, ByRef IsExpansion As Boolean)
	'
	'    'Debug.Print "Statstring:"
	'    'Debug.Print DebugOutput(sIn)
	'
	'    Dim D2Classes(0 To 7) As String
	'        D2Classes(0) = "Unknown Class"
	'        D2Classes(1) = "Amazon"
	'        D2Classes(2) = "Sorceress"
	'        D2Classes(3) = "Necromancer"
	'        D2Classes(4) = "Paladin"
	'        D2Classes(5) = "Barbarian"
	'        D2Classes(6) = "Druid"
	'        D2Classes(7) = "Assassin"
	'
	'    Dim Current As Byte
	'
	'    If (LenB(sIn) > 0) Then
	'        Current = Asc(Mid$(sIn, 12, 1)) '// Class
	'            If Current > 7 Then
	'                'Debug.Print "Zeroing current. Old value: " & Hex(Current)
	'                Current = 0
	'            End If
	'
	'            ClassByte = Current
	'            'Debug.Print Current & "\"
	'            'Debug.Print ClassByte
	'            Class = D2Classes(Current)
	'
	'        Current = Asc(Mid$(sIn, 24, 1)) '// Level
	'            Level = Current
	'
	'
	'        Current = Asc(Mid$(sIn, 25, 1)) '// Chartype
	'
	'        IsHardcore = (Current And &H4)
	'
	'        IsLadder = (Current And &H40)
	'
	'        IsDead = (Current And &H8)
	'
	'        IsExpansion = (Current And &H20)
	'    End If
	'End Sub
End Class