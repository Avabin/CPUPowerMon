import QtQuick
import QtQuick.Layouts
import org.kde.plasma.components as PlasmaComponents3
import org.kde.kirigami as Kirigami

Item {
    id: full

    property bool available: false
    property real watts: 0
    property var history: []
    property string errorText: ""
    property bool everUpdated: false
    property int intervalMs: 1000
    property string command: ""

    readonly property int historySeconds: Math.round(60 * intervalMs / 1000)

    implicitWidth: Kirigami.Units.gridUnit * 20
    implicitHeight: Kirigami.Units.gridUnit * 13

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: Kirigami.Units.smallSpacing
        spacing: Kirigami.Units.smallSpacing

        PlasmaComponents3.Label {
            Layout.fillWidth: true
            text: i18n("CPU Power")
            horizontalAlignment: Text.AlignHCenter
            font.weight: Font.DemiBold
            font.pointSize: Kirigami.Theme.defaultFont.pointSize * 1.05
        }

        PowerChart {
            Layout.fillWidth: true
            Layout.fillHeight: true
            Layout.minimumHeight: Kirigami.Units.gridUnit * 5
            values: full.history
            active: full.available
        }

        RowLayout {
            Layout.fillWidth: true
            spacing: Kirigami.Units.smallSpacing

            Rectangle {
                Layout.preferredWidth: Math.max(3, Kirigami.Units.gridUnit / 6)
                Layout.preferredHeight: legendLabel.implicitHeight
                Layout.alignment: Qt.AlignVCenter
                radius: width / 2
                color: full.available
                    ? Kirigami.Theme.highlightColor
                    : Kirigami.Theme.disabledTextColor
            }

            PlasmaComponents3.Label {
                id: legendLabel
                text: i18n("Package Power")
                font.weight: Font.DemiBold
            }

            Item {
                Layout.fillWidth: true
            }

            PlasmaComponents3.Label {
                text: full.available ? i18n("%1 W", full.watts.toFixed(1)) : i18n("N/A")
                font.weight: Font.DemiBold
                color: full.available
                    ? Kirigami.Theme.textColor
                    : Kirigami.Theme.negativeTextColor
            }
        }

        PlasmaComponents3.Label {
            Layout.fillWidth: true
            visible: !full.available
            text: full.errorText.length > 0
                ? full.errorText
                : i18n("Waiting for data from %1", full.command)
            color: Kirigami.Theme.negativeTextColor
            wrapMode: Text.WordWrap
            font.pointSize: Kirigami.Theme.smallFont.pointSize
        }

        RowLayout {
            Layout.fillWidth: true

            PlasmaComponents3.Label {
                text: i18n("History")
                opacity: 0.6
                font.pointSize: Kirigami.Theme.smallFont.pointSize
            }

            Item {
                Layout.fillWidth: true
            }

            PlasmaComponents3.Label {
                text: full.historySeconds >= 60
                    ? i18n("%1 min", Math.round(full.historySeconds / 60))
                    : i18n("%1 s", full.historySeconds)
                opacity: 0.8
                font.pointSize: Kirigami.Theme.smallFont.pointSize
            }
        }

        RowLayout {
            Layout.fillWidth: true

            PlasmaComponents3.Label {
                text: i18n("Polling interval")
                opacity: 0.6
                font.pointSize: Kirigami.Theme.smallFont.pointSize
            }

            Item {
                Layout.fillWidth: true
            }

            PlasmaComponents3.Label {
                text: i18n("%1 ms", full.intervalMs)
                opacity: 0.8
                font.pointSize: Kirigami.Theme.smallFont.pointSize
            }
        }
    }
}
