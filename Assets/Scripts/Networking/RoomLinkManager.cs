using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AuraSync.Networking
{
    public static class RoomLinkManager
    {
        public const string BaseJoinUrl = "https://aurasync.app/join?room=";

        public static string CreateInviteLink(string roomCode)
        {
            string code = RoomCodeGenerator.FormatCode(roomCode);

            if (!RoomCodeGenerator.IsValidCode(code))
                return "";

            return BaseJoinUrl + code;
        }

        public static string ExtractRoomCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            input = input.Trim();

            Match match = Regex.Match(
                input,
                @"AURA-[A-Z0-9]{4}",
                RegexOptions.IgnoreCase
            );

            if (match.Success)
                return match.Value.ToUpperInvariant();

            return RoomCodeGenerator.FormatCode(input);
        }

        public static void CopyInviteLink(string roomCode)
        {
            string link = CreateInviteLink(roomCode);

            if (!string.IsNullOrEmpty(link))
            {
                GUIUtility.systemCopyBuffer = link;
                Debug.Log("[AuraSync Invite] Copied invite link: " + link);
            }
        }
    }
}
