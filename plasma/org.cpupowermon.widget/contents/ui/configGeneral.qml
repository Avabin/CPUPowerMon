import QtQuick
import org.kde.kirigami as Kirigami
import org.kde.plasma.components as PlasmaComponents3

Kirigami.FormLayout {
    property alias cfg_command: commandField.text
    property alias cfg_intervalMs: intervalSpin.value

    PlasmaComponents3.TextField {
        id: commandField
        Kirigami.FormData.label: i18n("Command:")
        placeholderText: "/usr/local/bin/cpumonctl"
    }

    PlasmaComponents3.SpinBox {
        id: intervalSpin
        Kirigami.FormData.label: i18n("Polling interval (ms):")
        from: 1000
        to: 60000
        stepSize: 500
    }

    PlasmaComponents3.Label {
        text: i18n("The command must print the current power in watts on stdout. The executable data engine runs it once per interval.")
        wrapMode: Text.WordWrap
    }
}
