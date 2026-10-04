#include "sys.h"

void AppTask_DigitalIo(void *argument)
{
  DataHubDigitalIoData io_snapshot = {0};
  DIGITAL_IO_SERVICE_InputState input_state;
  TickType_t last_wake_tick;
  uint32_t last_publish_ms = 0U;

  (void)argument;
  last_wake_tick = xTaskGetTickCount();

  for (;;)
  {
    uint32_t now_ms = HAL_GetTick();

    /* 1 ms节拍扩展硬件计数、执行普通输入消抖并计算频率。 */
    DIGITAL_IO_SERVICE_ProcessInputs(now_ms);

    /*
     * DataHub消费者的最短周期为10 ms，因此无需每1 ms复制大快照。
     * 高速脉冲仍由定时器在硬件中逐边沿计数，此处只降低数据发布开销。
     */
    if ((uint32_t)(now_ms - last_publish_ms) >= 10U)
    {
      DIGITAL_IO_SERVICE_GetInputState(&input_state);

      io_snapshot.timestamp_ms = input_state.timestamp_ms;
      io_snapshot.input_mask = input_state.input_mask;
      io_snapshot.normal_input_mask = input_state.normal_input_mask;
      io_snapshot.output_mask = input_state.output_mask;
      io_snapshot.pulse_count[0] = input_state.pulse_count[0];
      io_snapshot.pulse_count[1] = input_state.pulse_count[1];
      io_snapshot.pulse_frequency_hz[0] = input_state.pulse_frequency_hz[0];
      io_snapshot.pulse_frequency_hz[1] = input_state.pulse_frequency_hz[1];
      io_snapshot.encoder_position = input_state.encoder_position;
      io_snapshot.encoder_speed_cps = input_state.encoder_speed_cps;
      io_snapshot.encoder_index_position = input_state.encoder_index_position;
      io_snapshot.encoder_direction = input_state.encoder_direction;
      io_snapshot.encoder_ab_state = input_state.encoder_ab_state;
      io_snapshot.encoder_index_count = input_state.encoder_index_count;
      io_snapshot.encoder_error_count = input_state.encoder_error_count;
      io_snapshot.status_flags = input_state.status_flags;
      DataHub_PublishDigitalIo(&io_snapshot);
      last_publish_ms = now_ms;
    }

    vTaskDelayUntil(&last_wake_tick, pdMS_TO_TICKS(1U));
  }
}
