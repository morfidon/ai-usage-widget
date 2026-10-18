using System.Windows;
using System.Windows.Markup;

namespace AIUsageWidget;

public static class WidgetTheme
{
    public static void Apply(Window window)
    {
        var resources = (ResourceDictionary)XamlReader.Parse("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="ToolTip">
    <Setter Property="Background" Value="#202B39"/><Setter Property="Foreground" Value="#EDF2F7"/>
    <Setter Property="BorderBrush" Value="#45576D"/><Setter Property="Padding" Value="13,11"/><Setter Property="FontSize" Value="12"/>
    <Setter Property="FontFamily" Value="Segoe UI"/><Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToolTip">
      <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
        <ContentPresenter/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="Expander">
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Expander">
      <StackPanel>
        <ToggleButton x:Name="HeaderToggle" Foreground="{TemplateBinding Foreground}" HorizontalContentAlignment="Left" IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
          <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
            <Border x:Name="HitArea" Background="Transparent" CornerRadius="4" Padding="0,5">
              <DockPanel><TextBlock x:Name="Arrow" Text="›" FontSize="17" Foreground="{TemplateBinding Foreground}" Margin="0,0,8,0" VerticalAlignment="Center"/><ContentPresenter VerticalAlignment="Center"/></DockPanel>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="Text" Value="⌄"/></Trigger>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="HitArea" Property="Background" Value="#263343"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="HitArea" Property="Background" Value="#30455F"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></ToggleButton.Template>
          <ContentPresenter ContentSource="Header"/>
        </ToggleButton>
        <ContentPresenter x:Name="Body" Visibility="Collapsed"/>
      </StackPanel>
      <ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="Body" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="ProgressBar">
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ProgressBar">
      <Border Background="{TemplateBinding Background}" CornerRadius="3"><Grid x:Name="PART_Track"><Border x:Name="PART_Indicator" HorizontalAlignment="Left" Background="{TemplateBinding Foreground}" CornerRadius="3"/></Grid></Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="TabItem">
    <Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#ACB8C8"/>
    <Setter Property="Padding" Value="14,9"/><Setter Property="Margin" Value="0,0,6,0"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem">
      <Border Background="{TemplateBinding Background}" CornerRadius="7" Padding="{TemplateBinding Padding}">
        <ContentPresenter ContentSource="Header" HorizontalAlignment="Center" VerticalAlignment="Center"/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#263343"/></Trigger>
      <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#2C405B"/><Setter Property="Foreground" Value="#EDF2F7"/></Trigger>
      <Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
    </Style.Triggers>
  </Style>
  <Style TargetType="ComboBoxItem">
    <Setter Property="Background" Value="#252E3A"/><Setter Property="Foreground" Value="#EDF2F8"/><Setter Property="Padding" Value="10,7"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter Property="Background" Value="#3A4D68"/></Trigger></Style.Triggers>
  </Style>
  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="#EDF2F8"/><Setter Property="Background" Value="#252E3A"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
      <Grid>
        <ToggleButton Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
          <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#252E3A" BorderBrush="#405069" BorderThickness="1" CornerRadius="7" Padding="10,7"><Grid><TextBlock Text="⌄" Foreground="#ABB9CC" HorizontalAlignment="Right"/></Grid></Border></ControlTemplate></ToggleButton.Template>
        </ToggleButton>
        <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" Margin="10,7,28,7" IsHitTestVisible="False" VerticalAlignment="Center"/>
        <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
          <Border Background="#252E3A" BorderBrush="#405069" BorderThickness="1" CornerRadius="7" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" Padding="3"><ScrollViewer MaxHeight="280"><ItemsPresenter/></ScrollViewer></Border>
        </Popup>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="ScrollBar">
    <Setter Property="Width" Value="11"/><Setter Property="Background" Value="Transparent"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
      <Grid Background="Transparent"><Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="{TemplateBinding Orientation}" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" ViewportSize="{TemplateBinding ViewportSize}" Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
        <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#4B596D" CornerRadius="4" Margin="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
      </Track></Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>
""");
        window.Resources.MergedDictionaries.Add(resources);
    }
}
