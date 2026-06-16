#include "fdcan.h"

FDCAN_HandleTypeDef hfdcan1;

void MX_FDCAN1_Init(void)
{
  hfdcan1.Instance = FDCAN1;
  hfdcan1.Init.FrameFormat = FDCAN_FRAME_CLASSIC;
  hfdcan1.Init.Mode = FDCAN_MODE_NORMAL;
  hfdcan1.Init.AutoRetransmission = ENABLE;
  hfdcan1.Init.TransmitPause = DISABLE;
  hfdcan1.Init.ProtocolException = DISABLE;
  hfdcan1.Init.NominalPrescaler = 12U;
  hfdcan1.Init.NominalSyncJumpWidth = 2U;
  hfdcan1.Init.NominalTimeSeg1 = 13U;
  hfdcan1.Init.NominalTimeSeg2 = 2U;
  hfdcan1.Init.DataPrescaler = 12U;
  hfdcan1.Init.DataSyncJumpWidth = 2U;
  hfdcan1.Init.DataTimeSeg1 = 13U;
  hfdcan1.Init.DataTimeSeg2 = 2U;
  hfdcan1.Init.MessageRAMOffset = 0U;
  hfdcan1.Init.StdFiltersNbr = 1U;
  hfdcan1.Init.ExtFiltersNbr = 1U;
  hfdcan1.Init.RxFifo0ElmtsNbr = 8U;
  hfdcan1.Init.RxFifo0ElmtSize = FDCAN_DATA_BYTES_8;
  hfdcan1.Init.RxFifo1ElmtsNbr = 0U;
  hfdcan1.Init.RxFifo1ElmtSize = FDCAN_DATA_BYTES_8;
  hfdcan1.Init.RxBuffersNbr = 0U;
  hfdcan1.Init.RxBufferSize = FDCAN_DATA_BYTES_8;
  hfdcan1.Init.TxEventsNbr = 0U;
  hfdcan1.Init.TxBuffersNbr = 0U;
  hfdcan1.Init.TxFifoQueueElmtsNbr = 8U;
  hfdcan1.Init.TxFifoQueueMode = FDCAN_TX_FIFO_OPERATION;
  hfdcan1.Init.TxElmtSize = FDCAN_DATA_BYTES_8;

  if (HAL_FDCAN_Init(&hfdcan1) != HAL_OK)
  {
    Error_Handler();
  }
}

void HAL_FDCAN_MspInit(FDCAN_HandleTypeDef *fdcanHandle)
{
  GPIO_InitTypeDef GPIO_InitStruct = {0};
  RCC_PeriphCLKInitTypeDef PeriphClkInitStruct = {0};

  if (fdcanHandle->Instance == FDCAN1)
  {
    PeriphClkInitStruct.PeriphClockSelection = RCC_PERIPHCLK_FDCAN;
    PeriphClkInitStruct.FdcanClockSelection = RCC_FDCANCLKSOURCE_PLL;
    if (HAL_RCCEx_PeriphCLKConfig(&PeriphClkInitStruct) != HAL_OK)
    {
      Error_Handler();
    }

    __HAL_RCC_FDCAN_CLK_ENABLE();
    __HAL_RCC_GPIOA_CLK_ENABLE();
    __HAL_RCC_GPIOD_CLK_ENABLE();

    /*
     * FDCAN1 GPIO:
     *   PD0 -> FDCAN1_RX
     *   PA1 -> FDCAN1_TX
     */
    GPIO_InitStruct.Pin = GPIO_PIN_0;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
    GPIO_InitStruct.Alternate = GPIO_AF9_FDCAN1;
    HAL_GPIO_Init(GPIOD, &GPIO_InitStruct);

    GPIO_InitStruct.Pin = GPIO_PIN_1;
    HAL_GPIO_Init(GPIOA, &GPIO_InitStruct);

    HAL_NVIC_SetPriority(FDCAN1_IT0_IRQn, 5, 0);
    HAL_NVIC_EnableIRQ(FDCAN1_IT0_IRQn);
  }
}

void HAL_FDCAN_MspDeInit(FDCAN_HandleTypeDef *fdcanHandle)
{
  if (fdcanHandle->Instance == FDCAN1)
  {
    __HAL_RCC_FDCAN_CLK_DISABLE();

    HAL_GPIO_DeInit(GPIOD, GPIO_PIN_0);
    HAL_GPIO_DeInit(GPIOA, GPIO_PIN_1);

    HAL_NVIC_DisableIRQ(FDCAN1_IT0_IRQn);
  }
}
