#include "can_service.h"

#include <string.h>

#include "fdcan.h"

static CAN_SERVICE_Diagnostics can_diag;

static uint32_t can_len_to_dlc(uint8_t len)
{
  static const uint32_t dlc_table[CAN_SERVICE_MAX_DATA_LEN + 1U] = {
    FDCAN_DLC_BYTES_0,
    FDCAN_DLC_BYTES_1,
    FDCAN_DLC_BYTES_2,
    FDCAN_DLC_BYTES_3,
    FDCAN_DLC_BYTES_4,
    FDCAN_DLC_BYTES_5,
    FDCAN_DLC_BYTES_6,
    FDCAN_DLC_BYTES_7,
    FDCAN_DLC_BYTES_8
  };

  if (len > CAN_SERVICE_MAX_DATA_LEN)
  {
    len = CAN_SERVICE_MAX_DATA_LEN;
  }

  return dlc_table[len];
}

static uint8_t can_dlc_to_len(uint32_t dlc)
{
  if (dlc <= FDCAN_DLC_BYTES_8)
  {
    return (uint8_t)dlc;
  }

  return CAN_SERVICE_MAX_DATA_LEN;
}

void CAN_SERVICE_Init(void)
{
  FDCAN_FilterTypeDef filter;

  memset(&can_diag, 0, sizeof(can_diag));

  filter.IdType = FDCAN_STANDARD_ID;
  filter.FilterIndex = 0U;
  filter.FilterType = FDCAN_FILTER_MASK;
  filter.FilterConfig = FDCAN_FILTER_TO_RXFIFO0;
  filter.FilterID1 = 0x000U;
  filter.FilterID2 = 0x000U;
  if (HAL_FDCAN_ConfigFilter(&hfdcan1, &filter) != HAL_OK)
  {
    Error_Handler();
  }

  filter.IdType = FDCAN_EXTENDED_ID;
  filter.FilterIndex = 0U;
  filter.FilterType = FDCAN_FILTER_MASK;
  filter.FilterConfig = FDCAN_FILTER_TO_RXFIFO0;
  filter.FilterID1 = 0x00000000U;
  filter.FilterID2 = 0x00000000U;
  if (HAL_FDCAN_ConfigFilter(&hfdcan1, &filter) != HAL_OK)
  {
    Error_Handler();
  }

  if (HAL_FDCAN_ConfigGlobalFilter(&hfdcan1,
                                   FDCAN_ACCEPT_IN_RX_FIFO0,
                                   FDCAN_ACCEPT_IN_RX_FIFO0,
                                   FDCAN_REJECT_REMOTE,
                                   FDCAN_REJECT_REMOTE) != HAL_OK)
  {
    Error_Handler();
  }

  if (HAL_FDCAN_Start(&hfdcan1) != HAL_OK)
  {
    Error_Handler();
  }

  if (HAL_FDCAN_ActivateNotification(&hfdcan1,
                                     FDCAN_IT_RX_FIFO0_NEW_MESSAGE,
                                     0U) != HAL_OK)
  {
    Error_Handler();
  }
}

int CAN_SERVICE_Send(const CAN_SERVICE_Frame *frame)
{
  FDCAN_TxHeaderTypeDef tx_header;
  uint8_t tx_data[CAN_SERVICE_MAX_DATA_LEN] = {0};
  uint8_t len;

  if (frame == 0)
  {
    return 0;
  }

  if (((frame->is_extended == 0U) && (frame->id > 0x7FFU)) ||
      ((frame->is_extended != 0U) && (frame->id > 0x1FFFFFFFU)))
  {
    can_diag.tx_error_count++;
    return 0;
  }

  len = frame->len;
  if (len > CAN_SERVICE_MAX_DATA_LEN)
  {
    len = CAN_SERVICE_MAX_DATA_LEN;
  }

  tx_header.Identifier = frame->id;
  tx_header.IdType = (frame->is_extended != 0U) ? FDCAN_EXTENDED_ID : FDCAN_STANDARD_ID;
  tx_header.TxFrameType = FDCAN_DATA_FRAME;
  tx_header.DataLength = can_len_to_dlc(len);
  tx_header.ErrorStateIndicator = FDCAN_ESI_ACTIVE;
  tx_header.BitRateSwitch = FDCAN_BRS_OFF;
  tx_header.FDFormat = FDCAN_CLASSIC_CAN;
  tx_header.TxEventFifoControl = FDCAN_NO_TX_EVENTS;
  tx_header.MessageMarker = 0U;

  memcpy(tx_data, frame->data, len);

  if (HAL_FDCAN_AddMessageToTxFifoQ(&hfdcan1, &tx_header, tx_data) != HAL_OK)
  {
    can_diag.tx_error_count++;
    return 0;
  }

  can_diag.tx_count++;
  can_diag.last_tx_tick = HAL_GetTick();
  can_diag.last_tx = *frame;
  can_diag.last_tx.len = len;

  return 1;
}

int CAN_SERVICE_SendStd(uint32_t std_id, const uint8_t *data, uint8_t len)
{
  CAN_SERVICE_Frame frame;

  memset(&frame, 0, sizeof(frame));
  frame.id = std_id & 0x7FFU;
  frame.is_extended = 0U;

  if (len > CAN_SERVICE_MAX_DATA_LEN)
  {
    len = CAN_SERVICE_MAX_DATA_LEN;
  }

  frame.len = len;
  if ((data != 0) && (len > 0U))
  {
    memcpy(frame.data, data, len);
  }

  return CAN_SERVICE_Send(&frame);
}

void CAN_SERVICE_GetDiagnostics(CAN_SERVICE_Diagnostics *diagnostics)
{
  if (diagnostics == 0)
  {
    return;
  }

  *diagnostics = can_diag;
}

void HAL_FDCAN_RxFifo0Callback(FDCAN_HandleTypeDef *hfdcan, uint32_t RxFifo0ITs)
{
  FDCAN_RxHeaderTypeDef rx_header;
  CAN_SERVICE_Frame frame;

  if ((hfdcan->Instance != FDCAN1) ||
      ((RxFifo0ITs & FDCAN_IT_RX_FIFO0_NEW_MESSAGE) == 0U))
  {
    return;
  }

  memset(&frame, 0, sizeof(frame));

  if (HAL_FDCAN_GetRxMessage(hfdcan, FDCAN_RX_FIFO0, &rx_header, frame.data) != HAL_OK)
  {
    can_diag.rx_error_count++;
    return;
  }

  frame.id = rx_header.Identifier;
  frame.is_extended = (rx_header.IdType == FDCAN_EXTENDED_ID) ? 1U : 0U;
  frame.len = can_dlc_to_len(rx_header.DataLength);

  can_diag.rx_count++;
  can_diag.last_rx_tick = HAL_GetTick();
  can_diag.last_rx = frame;

  if (CAN_SERVICE_Send(&frame) != 0)
  {
    can_diag.echo_count++;
  }
}
