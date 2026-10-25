import QtQuick
import QtQuick.Layouts
import org.kde.plasma.components as PlasmaComponents3
import org.kde.kirigami as Kirigami

Item {
    id: compact

    property bool available: false
    property real watts: 0
    property var history: []

    implicitWidth: row.implicitWidth + Kirigami.Units.smallSpacing * 2
    implicitHeight: row.implicitHeight + Kirigami.Units.smallSpacing

    RowLayout {
        id: row
        anchors.centerIn: parent
        spacing: Kirigami.Units.smallSpacing / 2

        PlasmaComponents3.Label {
            text: compact.available ? compact.watts.toFixed(1) : "–"
            color: compact.available ? Kirigami.Theme.textColor : Kirigami.Theme.negativeTextColor
            font.bold: true
        }

        PlasmaComponents3.Label {
            text: i18n("W")
            opacity: 0.75
        }

        Sparkline {
            Layout.preferredWidth: Kirigami.Units.gridUnit * 3
            Layout.preferredHeight: Kirigami.Units.gridUnit * 1.2
            values: compact.history
            active: compact.available
            opacity: compact.available ? 1.0 : 0.5
        }
    }
}
