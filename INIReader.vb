Imports System.IO
Imports System.Text
Imports System.Collections.Generic

Public Class IniFileReader
    Private ReadOnly _filePath As String
    Private ReadOnly _sections As New Dictionary(Of String, Dictionary(Of String, String))

    Public Sub New(filePath As String)
        _filePath = filePath
        ParseIniFile()
    End Sub

    Private Sub ParseIniFile()
        If Not File.Exists(_filePath) Then Return

        Dim currentSection As String = Nothing
        Dim lines As String() = File.ReadAllLines(_filePath, Encoding.UTF8)

        For Each line As String In lines
            Dim trimmedLine As String = line.Trim()

            ' 跳过空行和注释
            If String.IsNullOrEmpty(trimmedLine) OrElse trimmedLine.StartsWith(";") Then
                Continue For
            End If

            ' 处理节
            If trimmedLine.StartsWith("[") AndAlso trimmedLine.EndsWith("]") Then
                currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2)
                If Not _sections.ContainsKey(currentSection) Then
                    _sections(currentSection) = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                End If
                Continue For
            End If

            ' 处理键值对
            If currentSection IsNot Nothing Then
                Dim separatorIndex As Integer = trimmedLine.IndexOf("="c)
                If separatorIndex > 0 Then
                    Dim key As String = trimmedLine.Substring(0, separatorIndex).Trim()
                    Dim value As String = trimmedLine.Substring(separatorIndex + 1).Trim()

                    ' 处理值中的注释
                    Dim commentIndex As Integer = value.IndexOf(";"c)
                    If commentIndex >= 0 Then
                        value = value.Substring(0, commentIndex).Trim()
                    End If

                    _sections(currentSection)(key) = value
                End If
            End If
        Next
    End Sub

    Public Function GetValue(section As String, key As String, Optional defaultValue As String = Nothing) As String
        If _sections.ContainsKey(section) AndAlso _sections(section).ContainsKey(key) Then
            Return _sections(section)(key)
        End If
        Return defaultValue
    End Function

    Public Function GetSection(section As String) As Dictionary(Of String, String)
        If _sections.ContainsKey(section) Then
            Return _sections(section)
        End If
        Return New Dictionary(Of String, String)()
    End Function

    Public Sub WriteValue(section As String, key As String, value As String)
        ' 如果节不存在则创建
        If Not _sections.ContainsKey(section) Then
            _sections(section) = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        End If

        ' 更新值
        _sections(section)(key) = value

        ' 保存到文件
        SaveToFile()
    End Sub

    Private Sub SaveToFile()
        Dim content As New StringBuilder()

        For Each section In _sections
            content.AppendLine($"[{section.Key}]")

            For Each kvp In section.Value
                content.AppendLine($"{kvp.Key}={kvp.Value}")
            Next

            content.AppendLine() ' 添加空行分隔
        Next

        File.WriteAllText(_filePath, content.ToString(), Encoding.UTF8)
    End Sub
End Class