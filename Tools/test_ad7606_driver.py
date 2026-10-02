from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]


class Ad7606DriverContractTests(unittest.TestCase):
    def test_board_pin_map_matches_schematic(self):
        pins = (ROOT / "BSP/Inc/board_pins.h").read_text(encoding="utf-8")
        expected = {
            "BOARD_AD7606_OS0_Pin": "GPIO_PIN_4",
            "BOARD_AD7606_OS1_Pin": "GPIO_PIN_5",
            "BOARD_AD7606_OS2_Pin": "GPIO_PIN_6",
            "BOARD_AD7606_RANGE_Pin": "GPIO_PIN_7",
            "BOARD_AD7606_CONVST_Pin": "GPIO_PIN_8",
            "BOARD_AD7606_RESET_Pin": "GPIO_PIN_9",
            "BOARD_AD7606_SCLK_Pin": "GPIO_PIN_3",
            "BOARD_AD7606_DOUTA_Pin": "GPIO_PIN_5",
            "BOARD_AD7606_DOUTB_Pin": "GPIO_PIN_4",
            "BOARD_AD7606_CS_Pin": "GPIO_PIN_6",
            "BOARD_AD7606_BUSY_Pin": "GPIO_PIN_7",
        }
        for name, pin in expected.items():
            self.assertIn(f"#define {name}", pins)
            self.assertIn(pin, pins[pins.index(name):pins.index(name) + 100])

    def test_driver_uses_dual_serial_outputs(self):
        header = (ROOT / "BSP/Inc/bsp_ad7606.h").read_text(encoding="utf-8")
        source = (ROOT / "BSP/Src/bsp_ad7606.c").read_text(encoding="utf-8")
        self.assertNotIn("BSP_AD7606_DATA_MASK", header)
        self.assertIn("BSP_AD7606_DOUTA_Pin", source)
        self.assertIn("BSP_AD7606_DOUTB_Pin", source)
        self.assertIn("BSP_AD7606_RunSelfTest", source)

    def test_service_runs_and_reports_self_test(self):
        header = (ROOT / "Services/Inc/ad7606_service.h").read_text(encoding="utf-8")
        source = (ROOT / "Services/Src/ad7606_service.c").read_text(encoding="utf-8")
        self.assertIn("self_test", header)
        self.assertIn("BSP_AD7606_RunSelfTest", source)

    def test_minimal_modbus_mode_exposes_real_adc(self):
        main_h = (ROOT / "Core/Inc/main.h").read_text(encoding="utf-8")
        main_c = (ROOT / "Core/Src/main.c").read_text(encoding="utf-8")
        self.assertIn("#define MODBUS_REGISTER_TEST_MODE 0U", main_h)
        self.assertIn("AD7606_SERVICE_Init();", main_c)
        self.assertIn("xTaskCreate(Task_ad7606", main_c)
        self.assertIn("MX_USART2_UART_Init();", main_c)

    def test_debug_uart_reports_self_test_and_samples(self):
        source = (ROOT / "Services/Src/ad7606_service.c").read_text(encoding="utf-8")
        self.assertIn("[AD7606] self-test=", source)
        self.assertIn("ad7606_log_sample(\"first\")", source)
        self.assertIn(">= 1000U", source)
        self.assertIn("BUSY_PULLUP=%u", source)
        self.assertIn("BSP_AD7606_ProbeBusyWithPullup", source)
        self.assertIn("ad7606_log_sample(\"recovered\")", source)

    def test_standalone_diagnostic_bypasses_rtos_and_uses_direct_idr(self):
        main_h = (ROOT / "Core/Inc/main.h").read_text(encoding="utf-8")
        main_c = (ROOT / "Core/Src/main.c").read_text(encoding="utf-8")
        bsp_c = (ROOT / "BSP/Src/bsp_ad7606.c").read_text(encoding="utf-8")
        self.assertIn("#define APP_AD7606_DIAGNOSTIC_ONLY 1U", main_h)
        self.assertIn("AD7606_Diagnostic_Run();", main_c)
        self.assertIn("BSP_AD7606_BUSY_GPIO_Port->IDR", bsp_c)
        self.assertIn("DWT->CYCCNT", bsp_c)
        self.assertIn("scan_cycles", bsp_c)
        main_body = main_c[main_c.index("int main(void)"):]
        self.assertIn("#if APP_AD7606_DIAGNOSTIC_ONLY", main_body)
        declarations = main_c[:main_c.index("int main(void)")]
        self.assertNotIn("  AD7606_Diagnostic_Run();", declarations)


if __name__ == "__main__":
    unittest.main()
